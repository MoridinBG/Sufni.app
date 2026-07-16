namespace Sufni.Telemetry;

public sealed class FixedRateSegmentBuilder<T>(
    byte streamKind,
    byte? locationId,
    Action<RawStreamGap> addGap,
    int storageChunkSize = 1024)
{
    private readonly int storageChunkSize = storageChunkSize > 0
        ? storageChunkSize
        : throw new ArgumentOutOfRangeException(nameof(storageChunkSize));
    private List<T[]> currentSealedChunks = [];
    private T[] currentTail = [];
    private int currentTailCount;
    private int currentValueCount;
    private List<FixedRateSegment<T>> segments = [];
    private ulong currentFirstIndex;
    private ulong currentFirstMonotonicDeltaUs;
    private bool hasTimeline;
    private ulong lastIndex;

    public byte? LocationId => locationId;

    public IReadOnlyList<FixedRateSegment<T>> Segments => segments;

    public ulong Count { get; private set; }

    public ulong? LatestEndMonotonicDeltaUs { get; private set; }

    public FixedRateSegment<T>[] CreateSnapshot()
    {
        if (currentValueCount == 0)
        {
            return [.. segments];
        }

        var snapshot = new FixedRateSegment<T>[segments.Count + 1];
        for (var index = 0; index < segments.Count; index++)
        {
            snapshot[index] = segments[index];
        }

        snapshot[^1] = new FixedRateSegment<T>(
            currentFirstIndex,
            currentFirstMonotonicDeltaUs,
            CreateCurrentValuesSnapshot());
        return snapshot;
    }

    public void Clear()
    {
        ResetCurrentStorage();
        segments = [];
        currentFirstIndex = 0;
        currentFirstMonotonicDeltaUs = 0;
        Count = 0;
        LatestEndMonotonicDeltaUs = null;
        hasTimeline = false;
        lastIndex = 0;
    }

    public void AddValidSample(ulong index, ulong monotonicDeltaUs, T value, uint rateMhz)
    {
        var startsNewSegment = currentValueCount == 0;

        // Contiguous logical indices define the segment. Batch timestamps can jitter
        // around the nominal fixed-rate grid, so they must not split a valid run.
        if (hasTimeline && index != lastIndex + 1)
        {
            var missingCount = index > lastIndex ? index - lastIndex - 1 : 0;
            AddGap(lastIndex + 1, missingCount, SstV5CompactPayloadDecoder.RoundDurationUs(missingCount, rateMhz), "index_gap");
            Flush();
            startsNewSegment = true;
        }

        if (startsNewSegment)
        {
            currentFirstIndex = index;
            currentFirstMonotonicDeltaUs = monotonicDeltaUs;
        }

        AppendCurrentValue(value);
        var currentEndMonotonicDeltaUs = currentFirstMonotonicDeltaUs +
            SstV5CompactPayloadDecoder.RoundDurationUs((ulong)currentValueCount, rateMhz);
        LatestEndMonotonicDeltaUs = LatestEndMonotonicDeltaUs is ulong latestEndMonotonicDeltaUs
            ? Math.Max(latestEndMonotonicDeltaUs, currentEndMonotonicDeltaUs)
            : currentEndMonotonicDeltaUs;
        Count++;
        hasTimeline = true;
        lastIndex = index;
    }

    public void AddInvalidRange(ulong firstIndex, ulong count, ulong firstMonotonicDeltaUs, uint rateMhz)
    {
        if (count == 0)
        {
            return;
        }

        AddGap(firstIndex, count, SstV5CompactPayloadDecoder.RoundDurationUs(count, rateMhz), "invalid_validity");
        Flush();
        hasTimeline = true;
        lastIndex = checked(firstIndex + count - 1);
    }

    public void Finish()
    {
        Flush();
    }

    private void AddGap(ulong firstMissingIndex, ulong missingCount, ulong? missingTimeUs, string reason)
    {
        addGap(new RawStreamGap
        {
            StreamKind = streamKind,
            LocationId = locationId,
            FirstMissingIndex = firstMissingIndex,
            MissingCount = missingCount,
            MissingTimeUs = missingTimeUs,
            Reason = reason,
        });
    }

    private void Flush()
    {
        if (currentValueCount == 0)
        {
            return;
        }

        segments.Add(new FixedRateSegment<T>(
            currentFirstIndex,
            currentFirstMonotonicDeltaUs,
            TransferCurrentValues()));
        ResetCurrentStorage();
    }

    private void AppendCurrentValue(T value)
    {
        if (currentTailCount == currentTail.Length)
        {
            if (currentTail.Length > 0)
            {
                currentSealedChunks.Add(currentTail);
            }

            currentTail = new T[storageChunkSize];
            currentTailCount = 0;
        }

        currentTail[currentTailCount++] = value;
        currentValueCount++;
    }

    private FixedRateSegmentValues<T> CreateCurrentValuesSnapshot()
    {
        var sealedChunks = currentSealedChunks.Count == 0
            ? []
            : currentSealedChunks.ToArray();
        if (currentTailCount == currentTail.Length)
        {
            return new FixedRateSegmentValues<T>(
                sealedChunks,
                currentTail,
                currentTailCount,
                currentValueCount,
                storageChunkSize);
        }

        var tail = new T[currentTailCount];
        Array.Copy(currentTail, tail, currentTailCount);
        return new FixedRateSegmentValues<T>(
            sealedChunks,
            tail,
            currentTailCount,
            currentValueCount,
            storageChunkSize);
    }

    private FixedRateSegmentValues<T> TransferCurrentValues()
    {
        return new FixedRateSegmentValues<T>(
            currentSealedChunks.Count == 0 ? [] : currentSealedChunks.ToArray(),
            currentTail,
            currentTailCount,
            currentValueCount,
            storageChunkSize);
    }

    private void ResetCurrentStorage()
    {
        currentSealedChunks = [];
        currentTail = [];
        currentTailCount = 0;
        currentValueCount = 0;
    }
}

public sealed record FixedRateSegment<T>(
    ulong FirstIndex,
    ulong FirstMonotonicDeltaUs,
    FixedRateSegmentValues<T> Values);

public sealed class FixedRateSegmentValues<T> : IReadOnlyList<T>
{
    private readonly T[][] sealedChunks;
    private readonly T[] tail;
    private readonly int tailCount;
    private readonly int storageChunkSize;

    internal FixedRateSegmentValues(
        T[][] sealedChunks,
        T[] tail,
        int tailCount,
        int count,
        int storageChunkSize)
    {
        this.sealedChunks = sealedChunks;
        this.tail = tail;
        this.tailCount = tailCount;
        this.storageChunkSize = storageChunkSize;
        Count = count;
    }

    public int Count { get; }

    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
            var sealedValueCount = sealedChunks.Length * storageChunkSize;
            if (index < sealedValueCount)
            {
                return sealedChunks[index / storageChunkSize][index % storageChunkSize];
            }

            return tail[index - sealedValueCount];
        }
    }

    public T[] ToArray()
    {
        if (Count == 0)
        {
            return [];
        }

        var result = new T[Count];
        var offset = 0;
        foreach (var chunk in sealedChunks)
        {
            Array.Copy(chunk, 0, result, offset, chunk.Length);
            offset += chunk.Length;
        }

        if (tailCount > 0)
        {
            Array.Copy(tail, 0, result, offset, tailCount);
        }

        return result;
    }

    public IEnumerator<T> GetEnumerator()
    {
        foreach (var chunk in sealedChunks)
        {
            for (var index = 0; index < chunk.Length; index++)
            {
                yield return chunk[index];
            }
        }

        for (var index = 0; index < tailCount; index++)
        {
            yield return tail[index];
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

namespace Sufni.Telemetry;

public sealed class FixedRateSegmentBuilder<T>(
    byte streamKind,
    byte? locationId,
    Action<RawStreamGap> addGap)
{
    private readonly List<T> currentValues = [];
    private readonly List<FixedRateSegment<T>> segments = [];
    private ulong currentFirstIndex;
    private ulong currentFirstMonotonicDeltaUs;
    private bool hasTimeline;
    private ulong lastIndex;

    public byte? LocationId => locationId;

    public IReadOnlyList<FixedRateSegment<T>> Segments => segments;

    public ulong Count { get; private set; }

    public FixedRateSegment<T>[] CreateSnapshot()
    {
        if (currentValues.Count == 0)
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
            [.. currentValues]);
        return snapshot;
    }

    public void Clear()
    {
        currentValues.Clear();
        segments.Clear();
        currentFirstIndex = 0;
        currentFirstMonotonicDeltaUs = 0;
        Count = 0;
        hasTimeline = false;
        lastIndex = 0;
    }

    public void AddValidSample(ulong index, ulong monotonicDeltaUs, T value, uint rateMhz)
    {
        var startsNewSegment = currentValues.Count == 0;

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

        currentValues.Add(value);
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
        if (currentValues.Count == 0)
        {
            return;
        }

        segments.Add(new FixedRateSegment<T>(
            currentFirstIndex,
            currentFirstMonotonicDeltaUs,
            [.. currentValues]));
        currentValues.Clear();
    }
}

public sealed record FixedRateSegment<T>(
    ulong FirstIndex,
    ulong FirstMonotonicDeltaUs,
    T[] Values);

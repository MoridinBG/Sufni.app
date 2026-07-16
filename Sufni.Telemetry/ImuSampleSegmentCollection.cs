using System.Runtime.InteropServices;

namespace Sufni.Telemetry;

public readonly struct ImuSampleSegmentCollection
{
    private readonly RawImuData imuData;

    internal ImuSampleSegmentCollection(RawImuData imuData)
    {
        this.imuData = imuData;
    }

    public int Count => imuData.Segments.Count > 0
        ? imuData.Segments.Count
        : DenseLocationCount;

    public ImuSampleSegment this[int index]
    {
        get
        {
            if (imuData.Segments.Count > 0)
            {
                return new ImuSampleSegment(imuData.Segments[index]);
            }

            var locationCount = DenseLocationCount;
            if ((uint)index >= (uint)locationCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            var locationId = imuData.ActiveLocations.Count > 0
                ? imuData.ActiveLocations[index]
                : imuData.Meta[index].LocationId;
            return new ImuSampleSegment(imuData.Records, index, locationCount, locationId);
        }
    }

    public Enumerator GetEnumerator() => new(this);

    private int DenseLocationCount => imuData.Records.Count == 0
        ? 0
        : imuData.ActiveLocations.Count > 0
            ? imuData.ActiveLocations.Count
            : imuData.Meta.Count;

    public struct Enumerator
    {
        private readonly ImuSampleSegmentCollection segments;
        private int index;

        internal Enumerator(ImuSampleSegmentCollection segments)
        {
            this.segments = segments;
            index = -1;
        }

        public ImuSampleSegment Current => segments[index];

        public bool MoveNext() => ++index < segments.Count;
    }
}

public readonly struct ImuSampleSegment
{
    private readonly ImuRecord[]? records;
    private readonly List<ImuRecord>? denseRecords;
    private readonly int denseOffset;
    private readonly int denseStride;

    internal ImuSampleSegment(RawImuSegment segment)
    {
        LocationId = segment.LocationId;
        FirstIndex = segment.FirstIndex;
        FirstMonotonicDeltaUs = segment.FirstMonotonicDeltaUs;
        records = segment.Records;
        denseRecords = null;
        denseOffset = 0;
        denseStride = 1;
        Count = segment.Records.Length;
    }

    internal ImuSampleSegment(
        List<ImuRecord> denseRecords,
        int denseOffset,
        int denseStride,
        byte locationId)
    {
        LocationId = locationId;
        FirstIndex = 0;
        FirstMonotonicDeltaUs = 0;
        records = null;
        this.denseRecords = denseRecords;
        this.denseOffset = denseOffset;
        this.denseStride = denseStride;
        Count = denseOffset >= denseRecords.Count
            ? 0
            : (denseRecords.Count - 1 - denseOffset) / denseStride + 1;
    }

    public byte LocationId { get; }
    public ulong FirstIndex { get; }
    public ulong FirstMonotonicDeltaUs { get; }
    public int Count { get; }

    public ImuRecord this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return records is not null
                ? records[index]
                : denseRecords![denseOffset + index * denseStride];
        }
    }

    public bool TryGetContiguousRecords(out ReadOnlySpan<ImuRecord> contiguousRecords)
    {
        if (records is not null)
        {
            contiguousRecords = records;
            return true;
        }

        if (denseRecords is not null && denseStride == 1)
        {
            contiguousRecords = CollectionsMarshal.AsSpan(denseRecords).Slice(denseOffset, Count);
            return true;
        }

        contiguousRecords = default;
        return false;
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator
    {
        private readonly ImuSampleSegment segment;
        private int index;

        internal Enumerator(ImuSampleSegment segment)
        {
            this.segment = segment;
            index = -1;
        }

        public ImuRecord Current => segment[index];

        public bool MoveNext() => ++index < segment.Count;
    }
}

using MessagePack;

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public class RawCountSegment
{
    public ulong FirstIndex { get; set; }
    public ulong FirstMonotonicDeltaUs { get; set; }
    public ushort[] Counts { get; set; } = [];
}

[MessagePackObject(keyAsPropertyName: true)]
public class RawImuSegment
{
    public byte LocationId { get; set; }
    public ulong FirstIndex { get; set; }
    public ulong FirstMonotonicDeltaUs { get; set; }
    public ImuRecord[] Records { get; set; } = [];
}

[MessagePackObject(keyAsPropertyName: true)]
public class RawStreamGap
{
    public byte StreamKind { get; set; }
    public byte? LocationId { get; set; }
    public ulong FirstMissingIndex { get; set; }
    public ulong MissingCount { get; set; }
    public ulong? MissingTimeUs { get; set; }
    public string Reason { get; set; } = string.Empty;
}

[MessagePackObject(keyAsPropertyName: true)]
public class SstFinalStatus
{
    public byte SessionResultReason { get; set; }
    public ulong StoppedMonotonicDeltaUs { get; set; }
    public SstStreamFinalStatus[] Streams { get; set; } = [];
}

[MessagePackObject(keyAsPropertyName: true)]
public class SstStreamFinalStatus
{
    public byte StreamKind { get; set; }
    public byte ProducerState { get; set; }
    public byte ProducerFailureReason { get; set; }
    public ulong ProducerMissedCount { get; set; }
    public ulong ProducerMissingTimeUs { get; set; }
    public ulong SinkMissedCount { get; set; }
    public ulong SinkMissingTimeUs { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
public class ProcessedSuspensionSegment
{
    public int FirstDenseIndex { get; set; }
    public ulong FirstSourceIndex { get; set; }
    public double StartSeconds { get; set; }
    public double[] Travel { get; set; } = [];
    public double[] Velocity { get; set; } = [];
}

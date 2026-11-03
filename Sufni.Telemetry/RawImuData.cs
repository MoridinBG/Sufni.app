using System.Collections.Immutable;
using MessagePack;

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
[MessagePackFormatter(typeof(RawImuDataFormatter))]
public sealed record RawImuData
{
    public ImmutableArray<ImuMetaEntry> Meta { get; init; } = [];
    public int SampleRate { get; init; }
    public ImmutableArray<ImuRecord> Records { get; init; } = [];
    public ImmutableArray<byte> ActiveLocations { get; init; } = [];
    public ImmutableArray<RawImuSegment> Segments { get; init; } = [];
    public bool HasGaps { get; init; }
    [IgnoreMember] public bool HasSamples => Records.Length > 0 || Segments.Any(segment => segment.Records.Length > 0);
    [IgnoreMember] public ImuSampleSegmentCollection SampleSegments => new(this);
}

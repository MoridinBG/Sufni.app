using MessagePack;

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public class RawImuData
{
    public List<ImuMetaEntry> Meta { get; set; } = [];
    public int SampleRate { get; set; }
    public List<ImuRecord> Records { get; set; } = [];
    public List<byte> ActiveLocations { get; set; } = [];
    public List<RawImuSegment> Segments { get; set; } = [];
    public bool HasGaps { get; set; }
    [IgnoreMember] public bool HasSamples => Records.Count > 0 || Segments.Any(segment => segment.Records.Length > 0);
}

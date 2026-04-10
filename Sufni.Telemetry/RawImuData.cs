using MessagePack;

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public class RawImuData
{
    public List<ImuMetaEntry> Meta { get; set; } = [];
    public int SampleRate { get; set; }
    public List<ImuRecord> Records { get; set; } = [];
    public List<byte> ActiveLocations { get; set; } = [];
}

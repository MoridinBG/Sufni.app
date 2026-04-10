using MessagePack;

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public record ImuMetaEntry(
    byte LocationId,
    float AccelLsbPerG,
    float GyroLsbPerDps);

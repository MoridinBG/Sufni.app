using MessagePack;

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public readonly record struct ImuRecord(
    short Ax, short Ay, short Az,
    short Gx, short Gy, short Gz);

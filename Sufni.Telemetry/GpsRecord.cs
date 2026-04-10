using MessagePack;

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public record GpsRecord(
    DateTime Timestamp,
    double Latitude,
    double Longitude,
    float Altitude,
    float Speed,
    float Heading,
    byte FixMode,
    byte Satellites,
    float Epe2d,
    float Epe3d);
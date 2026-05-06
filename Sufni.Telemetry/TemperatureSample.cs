using MessagePack;

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public record TemperatureSample(
    long TimestampUtc,
    byte LocationId,
    float TemperatureCelsius);

[MessagePackObject(keyAsPropertyName: true)]
public record TemperatureAverage(
    byte LocationId,
    double TemperatureCelsius);

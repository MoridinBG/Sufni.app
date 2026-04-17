namespace Sufni.Telemetry;

public sealed record LiveTelemetryCapture(
    Metadata Metadata,
    BikeData BikeData,
    ushort[] FrontMeasurements,
    ushort[] RearMeasurements,
    RawImuData? ImuData,
    GpsRecord[]? GpsData,
    MarkerData[] Markers);
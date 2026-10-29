namespace Sufni.Telemetry;

public sealed record LiveTelemetryCapture(
    Metadata Metadata,
    BikeData BikeData,
    RawCountSegment[] FrontSegments,
    RawCountSegment[] RearSegments,
    RawImuData? ImuData,
    GpsRecord[]? GpsData,
    MarkerData[] Markers,
    RawStreamGap[] StreamGaps,
    SstFinalStatus? FinalStatus,
    bool MissingFinalStatus)
{
    public TemperatureSample[] TemperatureData { get; init; } = [];

    public ushort[] FrontMeasurements => FrontSegments.SelectMany(segment => segment.Counts).ToArray();
    public ushort[] RearMeasurements => RearSegments.SelectMany(segment => segment.Counts).ToArray();

    public LiveTelemetryCapture Slice(double startSeconds, double? endSeconds) =>
        TelemetrySlicer.Slice(this, startSeconds, endSeconds);
}

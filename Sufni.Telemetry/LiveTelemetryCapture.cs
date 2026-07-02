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
    public LiveTelemetryCapture(
        Metadata Metadata,
        BikeData BikeData,
        ushort[] FrontMeasurements,
        ushort[] RearMeasurements,
        RawImuData? ImuData,
        GpsRecord[]? GpsData,
        MarkerData[] Markers)
        : this(
            Metadata,
            BikeData,
            CreateDenseSegments(FrontMeasurements),
            CreateDenseSegments(RearMeasurements),
            ImuData,
            GpsData,
            Markers,
            [],
            null,
            false)
    {
    }

    public ushort[] FrontMeasurements => FrontSegments.SelectMany(segment => segment.Counts).ToArray();
    public ushort[] RearMeasurements => RearSegments.SelectMany(segment => segment.Counts).ToArray();

    private static RawCountSegment[] CreateDenseSegments(ushort[] counts) =>
        counts.Length == 0
            ? []
            :
            [
                new RawCountSegment
                {
                    FirstIndex = 0,
                    FirstMonotonicDeltaUs = 0,
                    Counts = counts,
                }
            ];
}

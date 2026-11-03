using System.Text.Json.Serialization;

namespace Sufni.Telemetry;

/// <summary>
/// JSON payload shape for a saved live-capture source.
/// It preserves raw capture data so processed telemetry can be rebuilt later.
/// </summary>
public sealed record RecordedLiveCaptureSourcePayload
{
    public static RecordedLiveCaptureSourcePayload FromCapture(LiveTelemetryCapture capture) => new()
    {
        SchemaVersion = 1,
        Metadata = capture.Metadata,
        FrontSegments = capture.FrontSegments,
        RearSegments = capture.RearSegments,
        ImuData = capture.ImuData,
        GpsData = capture.GpsData,
        TemperatureData = capture.TemperatureData,
        Markers = capture.Markers,
        StreamGaps = capture.StreamGaps,
        FinalStatus = capture.FinalStatus,
        MissingFinalStatus = capture.MissingFinalStatus,
    };

    public LiveTelemetryCapture ToCapture(BikeData bikeData)
    {
        var hasSegments = FrontSegments is not null || RearSegments is not null;
        return new LiveTelemetryCapture(
            Metadata, bikeData,
            hasSegments ? FrontSegments ?? [] : DenseSegments(FrontMeasurements ?? []),
            hasSegments ? RearSegments ?? [] : DenseSegments(RearMeasurements ?? []),
            ImuData, GpsData, Markers ?? [],
            hasSegments ? StreamGaps ?? [] : [],
            hasSegments ? FinalStatus : null,
            hasSegments && MissingFinalStatus == true)
        {
            TemperatureData = TemperatureData ?? [],
        };
    }

    private static RawCountSegment[] DenseSegments(ushort[] samples) =>
        samples.Length == 0 ? [] : [new RawCountSegment { Counts = samples }];

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("metadata")]
    public Metadata Metadata { get; init; } = new();

    [JsonPropertyName("front_measurements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ushort[]? FrontMeasurements { get; init; }

    [JsonPropertyName("rear_measurements")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ushort[]? RearMeasurements { get; init; }

    [JsonPropertyName("front_segments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RawCountSegment[]? FrontSegments { get; init; }

    [JsonPropertyName("rear_segments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RawCountSegment[]? RearSegments { get; init; }

    [JsonPropertyName("imu_data")]
    public RawImuData? ImuData { get; init; }

    [JsonPropertyName("gps_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GpsRecord[]? GpsData { get; init; }

    [JsonPropertyName("temperature_data")]
    public TemperatureSample[]? TemperatureData { get; init; } = [];

    [JsonPropertyName("markers")]
    public MarkerData[] Markers { get; init; } = [];

    [JsonPropertyName("stream_gaps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RawStreamGap[]? StreamGaps { get; init; }

    [JsonPropertyName("final_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SstFinalStatus? FinalStatus { get; init; }

    [JsonPropertyName("missing_final_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MissingFinalStatus { get; init; }
}

using System.Text.Json.Serialization;
using Sufni.Telemetry;

namespace Sufni.App.Models;

/// <summary>
/// JSON payload shape for a saved live-capture source.
/// It preserves the measured samples and optional IMU, GPS, and marker data
/// from the capture so processed telemetry can be rebuilt later.
/// </summary>
public sealed record RecordedLiveCaptureSourcePayload(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("metadata")] Metadata Metadata,
    [property: JsonPropertyName("front_measurements")] ushort[] FrontMeasurements,
    [property: JsonPropertyName("rear_measurements")] ushort[] RearMeasurements,
    [property: JsonPropertyName("imu_data")] RawImuData? ImuData,
    [property: JsonPropertyName("gps_data")] GpsRecord[]? GpsData,
    [property: JsonPropertyName("markers")] MarkerData[] Markers);

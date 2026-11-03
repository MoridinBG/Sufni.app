using MessagePack;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufni.Telemetry;

/// <summary>Owns processed telemetry and live-source serialization with legacy normalization.</summary>
public static class TelemetryCodec
{
    private static readonly TelemetryJsonContext JsonContext = new(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    });

    public static byte[] EncodeLiveSource(RecordedLiveCaptureSourcePayload source) =>
        JsonSerializer.SerializeToUtf8Bytes(source, JsonContext.RecordedLiveCaptureSourcePayload);

    public static RecordedLiveCaptureSourcePayload DecodeLiveSource(ReadOnlyMemory<byte> bytes) =>
        JsonSerializer.Deserialize(bytes.Span, JsonContext.RecordedLiveCaptureSourcePayload)
        ?? throw new JsonException("Recorded live-capture source payload is invalid.");

    public static byte[] Encode(TelemetryData telemetry) => MessagePackSerializer.Serialize(telemetry);

    public static TelemetryData Decode(ReadOnlyMemory<byte> bytes)
    {
        var value = MessagePackSerializer.Deserialize<TelemetryData>(bytes);
        var front = NormalizeSegmentSampleCounts(value.Front);
        var rear = NormalizeSegmentSampleCounts(value.Rear);
        if (ReferenceEquals(front, value.Front) && ReferenceEquals(rear, value.Rear)) return value;
        return value with { Front = front, Rear = rear };
    }

    private static Suspension NormalizeSegmentSampleCounts(Suspension suspension)
    {
        if (suspension is null) return null!;
        var segments = suspension.Segments;
        if (segments.IsDefaultOrEmpty)
            return segments.IsDefault ? suspension with { Segments = [] } : suspension;
        if (segments.All(segment => segment.SampleCount > 0)) return suspension;
        var length = suspension.Travel.IsDefault ? 0 : suspension.Travel.Length;
        return suspension with
        {
            Segments = [.. segments.Select((segment, index) => segment with
            {
                SampleCount = length == 0 ? 0 : index < segments.Length - 1
                    ? segments[index + 1].FirstDenseIndex - segment.FirstDenseIndex
                    : length - segment.FirstDenseIndex,
            })],
        };
    }
}

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(RecordedLiveCaptureSourcePayload))]
internal partial class TelemetryJsonContext : JsonSerializerContext;

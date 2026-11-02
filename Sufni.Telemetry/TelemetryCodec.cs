using MessagePack;

namespace Sufni.Telemetry;

/// <summary>Owns the persisted processed-telemetry format and legacy segment normalization.</summary>
public static class TelemetryCodec
{
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

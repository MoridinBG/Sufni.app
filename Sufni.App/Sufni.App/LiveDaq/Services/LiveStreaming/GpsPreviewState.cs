using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public sealed record GpsPreviewState(string StatusText)
{
    public static readonly GpsPreviewState NoFix = new("No GPS fix");

    public static GpsPreviewState FromRecord(GpsRecord? record)
    {
        if (record is null || record.FixMode == 0)
        {
            return NoFix;
        }

        return record.FixMode switch
        {
            1 => new GpsPreviewState("2D fix"),
            2 => new GpsPreviewState("3D fix"),
            _ => new GpsPreviewState($"Fix mode {record.FixMode}"),
        };
    }
}

using Sufni.Telemetry;

namespace Sufni.App.Services.LiveStreaming;

public sealed record GpsPreviewState(bool HasFix, bool IsReady, string StatusText)
{
    public static readonly GpsPreviewState NoFix = new(false, false, "No GPS fix");

    public static GpsPreviewState FromRecord(GpsRecord? record)
    {
        if (record is null || record.FixMode == 0)
        {
            return NoFix;
        }

        return record.FixMode switch
        {
            1 => new GpsPreviewState(true, true, "2D fix"),
            2 => new GpsPreviewState(true, true, "3D fix"),
            _ => new GpsPreviewState(true, true, $"Fix mode {record.FixMode}"),
        };
    }
}
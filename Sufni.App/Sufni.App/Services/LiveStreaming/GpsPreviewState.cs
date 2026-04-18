using Sufni.Telemetry;

namespace Sufni.App.Services.LiveStreaming;

public enum GpsFixKind
{
    None,
    TwoDimensional,
    ThreeDimensional,
    Other,
}

public sealed record GpsPreviewState(bool HasFix, bool IsReady, GpsFixKind FixKind, string StatusText)
{
    public static readonly GpsPreviewState NoFix = new(false, false, GpsFixKind.None, "No GPS fix");

    public static GpsPreviewState FromRecord(GpsRecord? record)
    {
        if (record is null || record.FixMode == 0)
        {
            return NoFix;
        }

        return record.FixMode switch
        {
            1 => new GpsPreviewState(true, false, GpsFixKind.TwoDimensional, "2D fix"),
            2 => new GpsPreviewState(true, true, GpsFixKind.ThreeDimensional, "3D fix"),
            _ => new GpsPreviewState(true, false, GpsFixKind.Other, $"Fix mode {record.FixMode}"),
        };
    }
}
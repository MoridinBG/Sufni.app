using Sufni.Telemetry;

namespace Sufni.App.Terrain.ViewModels;

internal static class TerrainResponseText
{
    public static string TargetLabel(ResponseTarget target) => target switch
    {
        ResponseTarget.FrontRms => "Front RMS",
        ResponseTarget.FrontPeakCompression => "Front peak compression",
        ResponseTarget.FrontPeakRebound => "Front peak rebound",
        ResponseTarget.RearRms => "Rear RMS",
        ResponseTarget.RearPeakCompression => "Rear peak compression",
        ResponseTarget.RearPeakRebound => "Rear peak rebound",
        ResponseTarget.Transmission => "Transmission",
        _ => throw new System.ArgumentOutOfRangeException(nameof(target))
    };

    public static string Value(ResponseCellState state, double? ratioValue) => ratioValue is { } ratio ? $"{ratio:0.00}×" : state switch
        {
            ResponseCellState.TooShort => "Under 300 m",
            ResponseCellState.InsufficientVariation => "Insufficient variation",
            ResponseCellState.NoBaseline => "Insufficient baseline",
            _ => "Unavailable"
        };
}

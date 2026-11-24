using System;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Formatting;

internal static class TerrainResponseText
{
    public static bool IsFork(ResponseTarget target) =>
        target is ResponseTarget.FrontRms or ResponseTarget.FrontPeakCompression or ResponseTarget.FrontPeakRebound;

    public static string Measure(ResponseTarget target) => target switch
    {
        ResponseTarget.FrontRms or ResponseTarget.RearRms => "Activity",
        ResponseTarget.FrontPeakCompression or ResponseTarget.RearPeakCompression => "Hardest compressions",
        ResponseTarget.FrontPeakRebound or ResponseTarget.RearPeakRebound => "Fastest rebounds",
        _ => throw new ArgumentOutOfRangeException(nameof(target))
    };

    public static string TargetLabel(ResponseTarget target) =>
        $"{(IsFork(target) ? "Fork" : "Shock")} {Measure(target).ToLowerInvariant()}";

    public static string TooShort { get; } = $"Under {SuspensionResponseModel.MinimumReportMetres} m";

    public static string Distance(double metres) =>
        metres is >= SuspensionResponseModel.MinimumReportMetres and < SuspensionResponseModel.ConfidentReportMetres
            ? $"{metres:0.#} m · low confidence" : $"{metres:0.#} m";

    /// <summary>A signed whole percent with a true minus sign: "−16%".</summary>
    public static string Percent(double deviation)
    {
        var percent = Math.Round(deviation * 100);
        return percent switch { > 0 => $"+{percent:0}%", < 0 => $"−{-percent:0}%", _ => "0%" };
    }

    public static string Value(ResponseCellState state, double? deviation) => deviation is { } value ? Percent(value) : state switch
    {
        ResponseCellState.TooShort => TooShort,
        ResponseCellState.InsufficientVariation => "Insufficient variation",
        ResponseCellState.NoBaseline => "Insufficient baseline",
        _ => "Unavailable"
    };
}

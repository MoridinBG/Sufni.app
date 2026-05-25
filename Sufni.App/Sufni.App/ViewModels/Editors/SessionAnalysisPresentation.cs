using System.Collections.Generic;
using System.Linq;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

internal static class SessionAnalysisPresentation
{
    public static IReadOnlyList<TravelHistogramModeOption> TravelHistogramModeOptions { get; } =
    [
        new(TravelHistogramMode.ActiveSuspension, "Active suspension", "Uses only compression and rebound stroke samples. Best for travel use while the suspension is actively moving."),
        new(TravelHistogramMode.DynamicSag, "Dynamic sag", "Uses every selected travel sample. Best for ride height over the segment, including quiet or steady sections."),
    ];

    public static IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } =
    [
        new(BalanceDisplacementMode.Zenith, "Zenith", "Plots each stroke at its deepest travel."),
        new(BalanceDisplacementMode.Travel, "Travel", "Plots each stroke by start-to-end travel distance."),
    ];

    public static IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } =
    [
        new(BalanceSpeedMode.Both, "Both", "Uses all matching compression or rebound strokes."),
        new(BalanceSpeedMode.LowSpeed, "Low speed", "Uses strokes below the high-speed threshold."),
        new(BalanceSpeedMode.HighSpeed, "High speed", "Uses strokes at or above the high-speed threshold."),
    ];

    public static IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } =
    [
        new(VelocityAverageMode.SampleAveraged, "Sample-averaged", "Counts every sample inside compression and rebound strokes. Best for where the damper spent time."),
        new(VelocityAverageMode.StrokePeakAveraged, "Stroke-peak average", "Counts each stroke once by peak velocity and peak travel. Best for what events the damper saw."),
    ];

    public static IReadOnlyList<SessionAnalysisTargetProfileOption> SessionAnalysisTargetProfileOptions { get; } =
    [
        new(SessionAnalysisTargetProfile.Weekend, "Weekend", "Uses conservative speed context for recreational pace and mixed terrain."),
        new(SessionAnalysisTargetProfile.Trail, "Trail", "Uses general trail-riding speed context."),
        new(SessionAnalysisTargetProfile.Enduro, "Enduro", "Uses faster rough-descending speed context."),
        new(SessionAnalysisTargetProfile.DH, "DH", "Uses downhill-race speed context."),
    ];

    public static string DescribeModes(
        TravelHistogramMode travelMode,
        VelocityAverageMode velocityMode,
        BalanceDisplacementMode displacementMode,
        BalanceSpeedMode speedMode)
    {
        return $"Travel: {DisplayName(travelMode)}  Velocity: {DisplayName(velocityMode)}  Balance: {DisplayName(displacementMode)} / {DisplayName(speedMode)}";
    }

    private static string DisplayName(TravelHistogramMode mode) =>
        TravelHistogramModeOptions.Single(option => option.Value == mode).DisplayName;

    private static string DisplayName(VelocityAverageMode mode) =>
        VelocityAverageModeOptions.Single(option => option.Value == mode).DisplayName;

    private static string DisplayName(BalanceDisplacementMode mode) =>
        BalanceDisplacementModeOptions.Single(option => option.Value == mode).DisplayName;

    private static string DisplayName(BalanceSpeedMode mode) =>
        BalanceSpeedModeOptions.Single(option => option.Value == mode).DisplayName;
}

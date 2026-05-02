using Sufni.Telemetry;

namespace Sufni.App.Models;

public enum PlotSmoothingLevel
{
    Off,
    Light,
    Strong,
}

public sealed record SessionPreferences(
    SessionPlotPreferences Plots,
    SessionStatisticsPreferences Statistics)
{
    public static SessionPreferences Default => new(new SessionPlotPreferences(), new SessionStatisticsPreferences());
}

public sealed record SessionPlotPreferences(
    bool Travel = true,
    bool Velocity = true,
    bool Imu = true,
    PlotSmoothingLevel TravelSmoothing = PlotSmoothingLevel.Off,
    PlotSmoothingLevel VelocitySmoothing = PlotSmoothingLevel.Off,
    PlotSmoothingLevel ImuSmoothing = PlotSmoothingLevel.Off);

public sealed record SessionStatisticsPreferences(
    TravelHistogramMode TravelHistogramMode = TravelHistogramMode.ActiveSuspension,
    VelocityAverageMode VelocityAverageMode = VelocityAverageMode.SampleAveraged,
    BalanceDisplacementMode BalanceDisplacementMode = BalanceDisplacementMode.Zenith,
    SessionAnalysisTargetProfile SessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Trail);

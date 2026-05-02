using Sufni.Telemetry;

namespace Sufni.App.Models;

public sealed record SessionPreferences(
    SessionPlotPreferences Plots,
    SessionStatisticsPreferences Statistics)
{
    public static SessionPreferences Default => new(new SessionPlotPreferences(), new SessionStatisticsPreferences());
}

public sealed record SessionPlotPreferences(
    bool Travel = true,
    bool Velocity = true,
    bool Imu = true);

public sealed record SessionStatisticsPreferences(
    TravelHistogramMode TravelHistogramMode = TravelHistogramMode.ActiveSuspension,
    VelocityAverageMode VelocityAverageMode = VelocityAverageMode.SampleAveraged,
    BalanceDisplacementMode BalanceDisplacementMode = BalanceDisplacementMode.Zenith,
    SessionAnalysisTargetProfile SessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Trail);

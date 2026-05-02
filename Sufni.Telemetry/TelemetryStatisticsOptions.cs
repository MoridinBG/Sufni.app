namespace Sufni.Telemetry;

public enum TravelHistogramMode
{
    ActiveSuspension = 0,
    DynamicSag = 1,
}

public enum BalanceDisplacementMode
{
    Zenith = 0,
    Travel = 1,
}

public enum VelocityAverageMode
{
    SampleAveraged = 0,
    StrokePeakAveraged = 1,
}

public sealed record TravelStatisticsOptions(
    TelemetryTimeRange? Range = null,
    TravelHistogramMode HistogramMode = TravelHistogramMode.ActiveSuspension);

public sealed record BalanceStatisticsOptions(
    TelemetryTimeRange? Range = null,
    BalanceDisplacementMode DisplacementMode = BalanceDisplacementMode.Zenith);

public sealed record VelocityStatisticsOptions(
    TelemetryTimeRange? Range = null,
    VelocityAverageMode VelocityAverageMode = VelocityAverageMode.SampleAveraged);
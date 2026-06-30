namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

// Neutral series role. The app renderer maps each role to a theme-invariant
// signal color, so extensions never name app theme colors directly.
public enum RecordedSessionGraphSeriesRole
{
    SuspensionFront,
    SuspensionRear,
    ImuFrame,
    ImuFork,
    ImuShock,
    GpsSpeed,
}

// One neutral series the app renders onto a recorded time-series plot.
// SecondsX / Values are paired samples (X in seconds, Y in the series' unit).
public sealed record RecordedSessionGraphSeries(
    RecordedSessionGraphSeriesRole Role,
    string Label,
    string Unit,
    double[] SecondsX,
    double[] Values,
    string Format = "0.##");

// A time span (seconds) the app may render as an airtime overlay region.
public sealed record RecordedSessionGraphSpan(double StartSeconds, double EndSeconds);

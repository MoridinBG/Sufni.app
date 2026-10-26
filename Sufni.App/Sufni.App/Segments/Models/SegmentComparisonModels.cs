using System;
using System.Collections.Generic;
using System.Globalization;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Plots;
using Sufni.Telemetry;

namespace Sufni.App.Segments.Models;

/// <summary>
/// One match resampled onto the source session's timeline: the matched session's
/// signals at the source seconds the DTW time map pairs them with.
/// </summary>
public sealed class SegmentComparison(
    SegmentMatch match,
    double sourceDurationSeconds,
    IReadOnlyList<SegmentComparisonSeries> travelSeries,
    IReadOnlyList<SegmentComparisonSeries> velocitySeries,
    IReadOnlyList<SegmentComparisonSeries> imuSeries,
    IReadOnlyList<SegmentComparisonSeries> speedSeries,
    IReadOnlyList<SegmentMatchedAirtime> airtimes,
    IReadOnlyList<TrackPoint> matchedSegmentTrackPoints,
    TelemetryTimeRange? analysisRange)
{
    public SegmentMatch Match { get; } = match;
    public double SourceDurationSeconds { get; } = sourceDurationSeconds;
    public IReadOnlyList<SegmentComparisonSeries> TravelSeries { get; } = travelSeries;
    public IReadOnlyList<SegmentComparisonSeries> VelocitySeries { get; } = velocitySeries;
    public IReadOnlyList<SegmentComparisonSeries> ImuSeries { get; } = imuSeries;
    public IReadOnlyList<SegmentComparisonSeries> SpeedSeries { get; } = speedSeries;

    /// <summary>Airtimes of the matched session, in source seconds.</summary>
    public IReadOnlyList<SegmentMatchedAirtime> Airtimes { get; } = airtimes;

    public IReadOnlyList<TrackPoint> MatchedSegmentTrackPoints { get; } = matchedSegmentTrackPoints;

    /// <summary>The matched range on the matched session's own timeline.</summary>
    public TelemetryTimeRange? AnalysisRange { get; } = analysisRange;

    public bool CanCompareAnalysis => AnalysisRange is not null && (TravelSeries.Count > 0 || VelocitySeries.Count > 0);

    public DateTime? MatchedSegmentLocalTime => Match.SessionTimestamp is { } timestamp
        ? DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime.AddSeconds(Match.StartSeconds)
        : null;

    public IReadOnlyList<SegmentComparisonSeries> GetSeries(SegmentComparisonSignal signal) => signal switch
    {
        SegmentComparisonSignal.Travel => TravelSeries,
        SegmentComparisonSignal.Velocity => VelocitySeries,
        SegmentComparisonSignal.Imu => ImuSeries,
        SegmentComparisonSignal.Speed => SpeedSeries,
        _ => [],
    };
}

/// <summary>The analysis input of a compared match: its telemetry and its whole session track.</summary>
public sealed record SegmentComparisonDetail(
    TelemetryData AnalysisTelemetryData,
    IReadOnlyList<TrackPoint> MatchedSessionTrackPoints);

public enum SegmentComparisonSignal
{
    Travel,
    Velocity,
    Imu,
    Speed,
}

public readonly record struct SegmentMatchedAirtime(double StartSeconds, double EndSeconds);

public sealed record SegmentComparisonSeriesRun(double[] SourceSeconds, double[] Values);

/// <summary>
/// A matched signal in source seconds. <see cref="Runs"/>, when present, splits it
/// at gaps that must not be connected.
/// </summary>
public sealed record SegmentComparisonSeries(
    string Label,
    string Unit,
    SegmentComparisonSeriesRole Role,
    double[] SourceSeconds,
    double[] Values,
    string Format = "0.##",
    IReadOnlyList<SegmentComparisonSeriesRun>? Runs = null);

public enum SegmentComparisonSeriesRole
{
    FrontTravel,
    RearTravel,
    FrontVelocity,
    RearVelocity,
    FrameImu,
    ForkImu,
    ShockImu,
    Speed,
}

/// <summary>
/// The current analysis compared with one match: histogram and statistics overlays
/// for the travel and velocity distribution plots and the damping band deltas.
/// </summary>
public sealed record SegmentAnalysisComparison(
    SegmentComparison Comparison,
    SegmentComparisonDetail Detail,
    IReadOnlyList<SegmentAnalysisOverlay> Overlays,
    IReadOnlyList<SegmentDampingMetric> Metrics);

public enum SegmentAnalysisPlot
{
    TravelDistribution,
    VelocityDistribution,
}

public sealed record SegmentAnalysisOverlay(
    SegmentAnalysisPlot Plot,
    SuspensionType Side,
    AnalysisPlotOverlay Overlay);

/// <summary>
/// A damping band percentage of the match, with the current session's value minus
/// the match value as the delta.
/// </summary>
public sealed record SegmentDampingMetric(SuspensionType Side, DampingBand Band, double MatchValue, double? Delta)
{
    public string DisplayValue => string.Format(CultureInfo.InvariantCulture, "match {0:0.00}", MatchValue);

    public string? DeltaValue => Delta switch
    {
        > 0 => string.Format(CultureInfo.InvariantCulture, "+{0:0.00}", Delta),
        < 0 => string.Format(CultureInfo.InvariantCulture, "{0:0.00}", Delta),
        0 => "0.00",
        _ => null,
    };

    public bool HasDeltaValue => Delta is not null;
    public bool IsPositiveTone => Delta > 0;
    public bool IsNegativeTone => Delta < 0;
}

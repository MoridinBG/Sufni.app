using System.Collections.Generic;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

namespace Sufni.App.Sessions.Models;

public enum SessionInsightsCategory
{
    DataQuality,
    TravelUse,
    Packing,
    ForkDamping,
    RearDamping,
    Balance,
    Vibration,
}

public enum SessionInsightsSeverity
{
    Info,
    Watch,
    Action,
}

public enum SessionInsightsConfidence
{
    Low,
    Medium,
    High,
}

public enum SessionInsightsTargetProfile
{
    Weekend,
    Trail,
    Enduro,
    DH,
}

public enum SessionInsightsStepId
{
    Sag = 1,
    Fork = 2,
    Rear = 3,
    Balance = 4,
}

public enum SessionInsightsFindingId
{
    Unknown = 0,
    FullSessionInsights,
    ShortSelectedRange,
    NoStrokeStatistics,
    OneEndMissingStrokeData,
    BalanceUnavailable,
    LowStrokeCount,
    ShallowTravelUse,
    RepeatedBottomouts,
    DeepTravelUse,
    DynamicSagMismatch,
    ReboundPacking,
    SupportBeforeReboundDiagnosis,
    ResistingImpacts,
    ReboundSlowForProfileContext,
    ReboundFastForProfileContext,
    CompressionSpeedsSubdued,
    CompressionSpeedsHigh,
    BalanceContextLimited,
    BalanceSlopesDiverge,
    VibrationNotUsedForRecommendations,
    VibrationContext,
}

public enum AdjustmentComponent
{
    AirPressure,
    Preload,
    Tokens,
    HighSpeedCompression,
    LowSpeedCompression,
    HighSpeedRebound,
    LowSpeedRebound,
}

public enum AdjustmentDirection
{
    Add,
    Remove,
    Open,
    Close,
}

// Approachable status colour tone shared by verdict pills, finding severity,
// and metric chips. Neutral stays quiet; Watch/Action draw attention.
public enum SessionInsightsStatusTone
{
    Neutral,
    Watch,
    Action,
}

// How a metric value compares to its target band, so the chip can show an
// at-a-glance indicator instead of forcing the reader to compare numbers.
public enum SessionInsightsMetricStatus
{
    Neutral,
    Good,
    BelowTarget,
    AboveTarget,
}

public sealed record SessionInsightsTargetProfileOption(
    SessionInsightsTargetProfile Value,
    string DisplayName,
    string Description);

public sealed record SessionInsightsRequest(
    TelemetryData? TelemetryData,
    TelemetryTimeRange? AnalysisRange,
    TravelDistributionMode TravelDistributionMode,
    VelocityAverageMode VelocityAverageMode,
    BalanceDisplacementMode BalanceDisplacementMode,
    BalanceSpeedMode BalanceSpeedMode,
    SessionDampingPercentages DampingPercentages,
    SessionInsightsTargetProfile TargetProfile)
{
    public DampingSpeedCutoffs DampingSpeedCutoffs { get; init; } = DampingSpeedCutoffs.Default;
}

public sealed record SessionInsightsResult(
    SurfacePresentationState State,
    IReadOnlyList<SessionInsightsStep> Steps,
    IReadOnlyList<SessionInsightsFinding> DataQualityFindings,
    SessionInsightsVibrationPanel? Vibration,
    IReadOnlyList<SessionInsightsFinding> AllFindings)
{
    public SessionInsightsResult(
        SurfacePresentationState state,
        IReadOnlyList<SessionInsightsFinding> findings)
        : this(state, [], [], null, findings)
    {
    }

    public static SessionInsightsResult Hidden { get; } = new(SurfacePresentationState.Hidden, [], [], null, []);

    // The single most important thing to try across the whole tab, so a less
    // experienced rider is not faced with four simultaneous experiments.
    public SessionInsightsNextStep? NextStep { get; init; }

    public IReadOnlyList<SessionInsightsFinding> Findings => AllFindings;

    public bool HasDataQualityFindings => DataQualityFindings.Count > 0;

    public bool HasNextStep => NextStep is not null;

    public bool HasVibration => Vibration is not null;
}

// The headline "Try this next" recommendation: which area it belongs to, the
// change to make, and why the data points to it.
public sealed record SessionInsightsNextStep(
    string Area,
    Adjustment Adjustment,
    string Rationale);

public sealed record SessionInsightsFinding(
    SessionInsightsCategory Category,
    SessionInsightsSeverity Severity,
    SessionInsightsConfidence Confidence,
    string Title,
    string Observation,
    string Recommendation,
    IReadOnlyList<SessionInsightsEvidence> Evidence,
    IReadOnlyList<Adjustment> Adjustments)
{
    public SessionInsightsFindingId Id { get; init; } = SessionInsightsFindingId.Unknown;

    public SessionInsightsFinding(
        SessionInsightsFindingId id,
        SessionInsightsCategory category,
        SessionInsightsSeverity severity,
        SessionInsightsConfidence confidence,
        string title,
        string observation,
        string recommendation,
        IReadOnlyList<SessionInsightsEvidence> evidence,
        IReadOnlyList<Adjustment> adjustments)
        : this(category, severity, confidence, title, observation, recommendation, evidence, adjustments)
    {
        Id = id;
    }

    public SessionInsightsFinding(
        SessionInsightsCategory category,
        SessionInsightsSeverity severity,
        SessionInsightsConfidence confidence,
        string title,
        string observation,
        string recommendation,
        IReadOnlyList<SessionInsightsEvidence> evidence)
        : this(category, severity, confidence, title, observation, recommendation, evidence, [])
    {
    }

    public SessionInsightsFinding(
        SessionInsightsFindingId id,
        SessionInsightsCategory category,
        SessionInsightsSeverity severity,
        SessionInsightsConfidence confidence,
        string title,
        string observation,
        string recommendation,
        IReadOnlyList<SessionInsightsEvidence> evidence)
        : this(id, category, severity, confidence, title, observation, recommendation, evidence, [])
    {
    }

    public string SeverityText => Severity switch
    {
        SessionInsightsSeverity.Action => "Change recommended",
        SessionInsightsSeverity.Watch => "Worth checking",
        _ => "Note",
    };

    public SessionInsightsStatusTone Tone => Severity switch
    {
        SessionInsightsSeverity.Action => SessionInsightsStatusTone.Action,
        SessionInsightsSeverity.Watch => SessionInsightsStatusTone.Watch,
        _ => SessionInsightsStatusTone.Neutral,
    };

    public string CategoryText => Category switch
    {
        SessionInsightsCategory.DataQuality => "Data quality",
        SessionInsightsCategory.TravelUse => "Travel use",
        SessionInsightsCategory.Packing => "Packing",
        SessionInsightsCategory.ForkDamping => "Fork damping",
        SessionInsightsCategory.RearDamping => "Rear damping",
        SessionInsightsCategory.Balance => "Balance",
        SessionInsightsCategory.Vibration => "Vibration",
        _ => Category.ToString(),
    };

    public string ConfidenceText => $"{Confidence} confidence";

    // The single suggested change for this finding (priority order preserved by
    // the diagnostics). Findings that only add context expose none, and the
    // fallback Recommendation carries their guidance instead.
    public Adjustment? PrimarySuggestion => Adjustments.Count > 0 ? Adjustments[0] : null;

    public bool HasSuggestion => Adjustments.Count > 0;
}

public sealed record Adjustment(
    AdjustmentComponent Component,
    AdjustmentDirection Direction,
    string Magnitude,
    string Side,
    string ExpectedEffect,
    int Priority)
{
    public string ComponentName => Component switch
    {
        AdjustmentComponent.AirPressure => "air pressure",
        AdjustmentComponent.Preload => "preload",
        AdjustmentComponent.Tokens => "token",
        AdjustmentComponent.HighSpeedCompression => "HSC",
        AdjustmentComponent.LowSpeedCompression => "LSC",
        AdjustmentComponent.HighSpeedRebound => "HSR",
        AdjustmentComponent.LowSpeedRebound => "LSR",
        _ => Component.ToString(),
    };

    // Spelled-out damping name (with the acronym once in parentheses) for the
    // approachable experiment cards; compact ComponentName stays for chips.
    public string ComponentLongName => Component switch
    {
        AdjustmentComponent.AirPressure => "air pressure",
        AdjustmentComponent.Preload => "preload",
        AdjustmentComponent.Tokens => "volume token",
        AdjustmentComponent.HighSpeedCompression => "high-speed compression (HSC)",
        AdjustmentComponent.LowSpeedCompression => "low-speed compression (LSC)",
        AdjustmentComponent.HighSpeedRebound => "high-speed rebound (HSR)",
        AdjustmentComponent.LowSpeedRebound => "low-speed rebound (LSR)",
        _ => Component.ToString(),
    };

    public string DirectionVerb => Direction switch
    {
        AdjustmentDirection.Add => "Add",
        AdjustmentDirection.Remove => "Remove",
        AdjustmentDirection.Open => "Open",
        AdjustmentDirection.Close => "Close",
        _ => Direction.ToString(),
    };

    public string ExperimentText => Component switch
    {
        AdjustmentComponent.Tokens =>
            $"{Side} · {DirectionVerb} {Magnitude}",
        _ =>
            $"{Side} · {DirectionVerb} {ComponentLongName}, {Magnitude}",
    };

    public string SentenceText => Component switch
    {
        AdjustmentComponent.Tokens =>
            $"{Side}: {DirectionVerb.ToLowerInvariant()} {Magnitude}",
        _ =>
            $"{Side}: {DirectionVerb.ToLowerInvariant()} {ComponentLongName}, {Magnitude}",
    };
}

public sealed record SessionInsightsMetric(
    string Label,
    string Value,
    string? Unit,
    string? Side,
    string? TargetRange,
    SessionInsightsMetricStatus Status = SessionInsightsMetricStatus.Neutral,
    string? Tooltip = null)
{
    public string DisplayValue => string.IsNullOrWhiteSpace(Unit) ? Value : $"{Value} {Unit}";

    // Only a value outside its target draws attention; an in-range or
    // non-comparable value stays visually quiet.
    public SessionInsightsStatusTone Tone => Status is SessionInsightsMetricStatus.BelowTarget or SessionInsightsMetricStatus.AboveTarget
        ? SessionInsightsStatusTone.Watch
        : SessionInsightsStatusTone.Neutral;

    public string StatusGlyph => Status switch
    {
        SessionInsightsMetricStatus.Good => "✔",        // heavy check
        SessionInsightsMetricStatus.BelowTarget => "▼", // down-pointing triangle
        SessionInsightsMetricStatus.AboveTarget => "▲", // up-pointing triangle
        _ => string.Empty,
    };

    public bool HasStatusGlyph => Status != SessionInsightsMetricStatus.Neutral;

    public bool HasTooltip => !string.IsNullOrWhiteSpace(Tooltip);
}

public sealed record SessionInsightsStep(
    SessionInsightsStepId Id,
    string Title,
    SessionInsightsSeverity Verdict,
    bool HasIssue,
    IReadOnlyList<SessionInsightsMetric> Metrics,
    IReadOnlyList<SessionInsightsFinding> Findings)
{
    // A later step is gated when an earlier step in the Sag -> Fork -> Rear ->
    // Balance sequence still has an unresolved issue; its numbers usually shift
    // once the earlier area is sorted.
    public string? GatingMessage { get; init; }

    public int Number => (int)Id;

    public string VerdictText => (HasIssue, Verdict) switch
    {
        (true, SessionInsightsSeverity.Action) => "Change recommended",
        (true, _) => "Worth checking",
        _ => "Looks good",
    };

    public SessionInsightsStatusTone VerdictTone => (HasIssue, Verdict) switch
    {
        (true, SessionInsightsSeverity.Action) => SessionInsightsStatusTone.Action,
        (true, _) => SessionInsightsStatusTone.Watch,
        _ => SessionInsightsStatusTone.Neutral,
    };

    public bool HasGatingMessage => !string.IsNullOrWhiteSpace(GatingMessage);

    public bool HasFindings => Findings.Count > 0;
}

public sealed record SessionInsightsVibrationPanel(
    IReadOnlyList<SessionInsightsMetric> Metrics,
    string Caveat)
{
    public bool HasMetrics => Metrics.Count > 0;
}

public sealed record SessionInsightsEvidence(
    string Label,
    string Value,
    string? Unit,
    string? Side,
    string SourceMode,
    string? Note = null)
{
    public string DisplayValue => string.IsNullOrWhiteSpace(Unit) ? Value : $"{Value} {Unit}";
}

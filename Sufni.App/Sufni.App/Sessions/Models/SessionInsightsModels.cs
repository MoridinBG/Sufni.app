using System.Collections.Generic;
using System.Linq;
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

    public IReadOnlyList<SessionInsightsFinding> Findings => AllFindings;

    public bool HasDataQualityFindings => DataQualityFindings.Count > 0;

    public bool HasVibration => Vibration is not null;
}

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
        AdjustmentComponent.AirPressure or AdjustmentComponent.Preload =>
            $"{Side} · {DirectionVerb} {ComponentName}, {Magnitude}",
        AdjustmentComponent.Tokens =>
            $"{Side} · {DirectionVerb} {Magnitude}",
        _ =>
            $"{Side} · {DirectionVerb} {ComponentName} by {Magnitude}",
    };

    public string SentenceText => Component switch
    {
        AdjustmentComponent.AirPressure or AdjustmentComponent.Preload =>
            $"{Side}: {DirectionVerb.ToLowerInvariant()} {ComponentName}, {Magnitude}",
        AdjustmentComponent.Tokens =>
            $"{Side}: {DirectionVerb.ToLowerInvariant()} {Magnitude}",
        _ =>
            $"{Side}: {DirectionVerb.ToLowerInvariant()} {ComponentName} by {Magnitude}",
    };
}

public sealed record SessionInsightsMetric(
    string Label,
    string Value,
    string? Unit,
    string? Side,
    string? TargetRange)
{
    public string DisplayValue => string.IsNullOrWhiteSpace(Unit) ? Value : $"{Value} {Unit}";
}

public sealed record SessionInsightsStep(
    SessionInsightsStepId Id,
    string Title,
    SessionInsightsSeverity Verdict,
    bool HasIssue,
    IReadOnlyList<SessionInsightsMetric> Metrics,
    Adjustment? PrimaryAdjustment,
    IReadOnlyList<Adjustment> Alternates,
    IReadOnlyList<SessionInsightsFinding> Findings)
{
    public int Number => (int)Id;

    public string VerdictText => HasIssue ? Verdict.ToString() : "OK";

    public bool HasPrimaryAdjustment => PrimaryAdjustment is not null;

    public bool HasAlternates => Alternates.Count > 0;

    public string OtherOptionsHeader => $"Other options ({Alternates.Count})";

    public bool HasFindings => Findings.Count > 0;

    public string FindingsHeader => $"Findings ({Findings.Count})";

    public string? ContextMessage => PrimaryAdjustment is null
        ? Findings
            .OrderByDescending(finding => finding.Severity)
            .ThenByDescending(finding => finding.Confidence)
            .FirstOrDefault()?.Recommendation
        : null;

    public bool HasContextMessage => !string.IsNullOrWhiteSpace(ContextMessage);
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

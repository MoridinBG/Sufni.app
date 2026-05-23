using System.Collections.Generic;
using System.Linq;
using Sufni.App.Presentation;
using Sufni.Telemetry;

namespace Sufni.App.Models;

public enum SessionAnalysisCategory
{
    DataQuality,
    TravelUse,
    Packing,
    ForkDamping,
    RearDamping,
    Balance,
    Vibration,
}

public enum SessionAnalysisSeverity
{
    Info,
    Watch,
    Action,
}

public enum SessionAnalysisConfidence
{
    Low,
    Medium,
    High,
}

public enum SessionAnalysisTargetProfile
{
    Weekend,
    Trail,
    Enduro,
    DH,
}

public enum SessionAnalysisStepId
{
    Sag = 1,
    Fork = 2,
    Rear = 3,
    Balance = 4,
}

public enum SessionAnalysisFindingId
{
    Unknown = 0,
    FullSessionAnalysis,
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

public sealed record SessionAnalysisTargetProfileOption(
    SessionAnalysisTargetProfile Value,
    string DisplayName,
    string Description);

public sealed record SessionAnalysisRequest(
    TelemetryData? TelemetryData,
    TelemetryTimeRange? AnalysisRange,
    TravelHistogramMode TravelHistogramMode,
    VelocityAverageMode VelocityAverageMode,
    BalanceDisplacementMode BalanceDisplacementMode,
    BalanceSpeedMode BalanceSpeedMode,
    SessionDamperPercentages DamperPercentages,
    SessionAnalysisTargetProfile TargetProfile);

public sealed record SessionAnalysisResult(
    SurfacePresentationState State,
    IReadOnlyList<SessionAnalysisStep> Steps,
    IReadOnlyList<SessionAnalysisFinding> DataQualityFindings,
    SessionAnalysisVibrationPanel? Vibration,
    IReadOnlyList<SessionAnalysisFinding> AllFindings)
{
    public SessionAnalysisResult(
        SurfacePresentationState state,
        IReadOnlyList<SessionAnalysisFinding> findings)
        : this(state, [], [], null, findings)
    {
    }

    public static SessionAnalysisResult Hidden { get; } = new(SurfacePresentationState.Hidden, [], [], null, []);

    public IReadOnlyList<SessionAnalysisFinding> Findings => AllFindings;

    public bool HasDataQualityFindings => DataQualityFindings.Count > 0;

    public bool HasVibration => Vibration is not null;
}

public sealed record SessionAnalysisFinding(
    SessionAnalysisCategory Category,
    SessionAnalysisSeverity Severity,
    SessionAnalysisConfidence Confidence,
    string Title,
    string Observation,
    string Recommendation,
    IReadOnlyList<SessionAnalysisEvidence> Evidence,
    IReadOnlyList<Adjustment> Adjustments)
{
    public SessionAnalysisFindingId Id { get; init; } = SessionAnalysisFindingId.Unknown;

    public SessionAnalysisFinding(
        SessionAnalysisFindingId id,
        SessionAnalysisCategory category,
        SessionAnalysisSeverity severity,
        SessionAnalysisConfidence confidence,
        string title,
        string observation,
        string recommendation,
        IReadOnlyList<SessionAnalysisEvidence> evidence,
        IReadOnlyList<Adjustment> adjustments)
        : this(category, severity, confidence, title, observation, recommendation, evidence, adjustments)
    {
        Id = id;
    }

    public SessionAnalysisFinding(
        SessionAnalysisCategory category,
        SessionAnalysisSeverity severity,
        SessionAnalysisConfidence confidence,
        string title,
        string observation,
        string recommendation,
        IReadOnlyList<SessionAnalysisEvidence> evidence)
        : this(category, severity, confidence, title, observation, recommendation, evidence, [])
    {
    }

    public SessionAnalysisFinding(
        SessionAnalysisFindingId id,
        SessionAnalysisCategory category,
        SessionAnalysisSeverity severity,
        SessionAnalysisConfidence confidence,
        string title,
        string observation,
        string recommendation,
        IReadOnlyList<SessionAnalysisEvidence> evidence)
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

public sealed record SessionAnalysisMetric(
    string Label,
    string Value,
    string? Unit,
    string? Side,
    string? TargetRange)
{
    public string DisplayValue => string.IsNullOrWhiteSpace(Unit) ? Value : $"{Value} {Unit}";
}

public sealed record SessionAnalysisStep(
    SessionAnalysisStepId Id,
    string Title,
    SessionAnalysisSeverity Verdict,
    bool HasIssue,
    IReadOnlyList<SessionAnalysisMetric> Metrics,
    Adjustment? PrimaryAdjustment,
    IReadOnlyList<Adjustment> Alternates,
    IReadOnlyList<SessionAnalysisFinding> Findings)
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

public sealed record SessionAnalysisVibrationPanel(
    IReadOnlyList<SessionAnalysisMetric> Metrics,
    string Caveat)
{
    public bool HasMetrics => Metrics.Count > 0;
}

public sealed record SessionAnalysisEvidence(
    string Label,
    string Value,
    string? Unit,
    string? Side,
    string SourceMode,
    string? Note = null)
{
    public string DisplayValue => string.IsNullOrWhiteSpace(Unit) ? Value : $"{Value} {Unit}";
}

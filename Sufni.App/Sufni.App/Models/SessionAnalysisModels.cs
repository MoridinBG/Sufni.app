using System.Collections.Generic;
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
    SessionDamperPercentages DamperPercentages,
    SessionAnalysisTargetProfile TargetProfile);

public sealed record SessionAnalysisResult(
    SurfacePresentationState State,
    IReadOnlyList<SessionAnalysisFinding> Findings)
{
    public static SessionAnalysisResult Hidden { get; } = new(SurfacePresentationState.Hidden, []);
}

public sealed record SessionAnalysisFinding(
    SessionAnalysisCategory Category,
    SessionAnalysisSeverity Severity,
    SessionAnalysisConfidence Confidence,
    string Title,
    string Observation,
    string Recommendation,
    IReadOnlyList<SessionAnalysisEvidence> Evidence);

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
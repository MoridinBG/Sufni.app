using System.Collections.Generic;
using Sufni.Telemetry;

using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Insights.Services.SessionInsights;

internal readonly record struct SpeedBand(double Low, double High);

internal readonly record struct ProfileReferences(SpeedBand Rebound, SpeedBand Compression);

internal sealed record AnalysisContext(
    SessionInsightsRequest Request,
    TravelStatisticsOptions TravelOptions,
    VelocityStatisticsOptions VelocityOptions,
    BalanceStatisticsOptions BalanceOptions,
    ProfileReferences Profile);

internal sealed record SideSnapshot(
    SuspensionType Type,
    bool HasStrokeData,
    TravelStatistics Travel,
    VelocityStatistics Velocity,
    double? MaxTravel,
    double? MaxTravelPercent,
    double? AverageTravelPercent,
    int StrokeCount);

/// <summary>
/// One member per distinct evidence quantity the heuristics emit. The text
/// catalog owns the label/unit/format/source-mode mapping for each member.
/// </summary>
internal enum DiagnosticMeasurement
{
    AnalysisRangeFullSession,
    RangeDurationSeconds,
    BalanceMode,
    StrokeCount,
    MaxTravelPercent,
    AverageTravelPercent,
    Bottomouts,
    HealthyHardSectionReference,
    CompressionP95,
    ReboundP95,
    CompressionReferenceBand,
    ReboundReferenceBand,
    DampingBandHsc,
    DampingBandLsc,
    DampingBandHsr,
    DampingBandLsr,
    CompressionBalanceSlopeDeltaPercent,
    ReboundBalanceSlopeDeltaPercent,
    FrontBalanceSlope,
    RearBalanceSlope,
    BalanceMeanSignedDeviation,
    BalanceContextLimit,
    MagicCarpetRatio,
    AverageG,
    CompressionVibrationPercent,
    ReboundVibrationPercent,
}

internal sealed record DiagnosticEvidence(
    DiagnosticMeasurement Measurement,
    SuspensionType? Side,
    double? Value);

internal sealed record DiagnosticAdjustment(
    AdjustmentComponent Component,
    AdjustmentDirection Direction,
    SuspensionType Side,
    int Priority);

internal sealed record DiagnosticFinding(
    SessionInsightsFindingId Id,
    SessionInsightsCategory Category,
    SessionInsightsSeverity Severity,
    SessionInsightsConfidence Confidence,
    IReadOnlyList<DiagnosticEvidence> Evidence,
    IReadOnlyList<DiagnosticAdjustment> Adjustments);

internal sealed record SessionDiagnosticsReport(
    SideSnapshot? Front,
    SideSnapshot? Rear,
    IReadOnlyList<DiagnosticFinding> Findings);

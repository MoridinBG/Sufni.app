using System;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public sealed record RecordedSessionHostState(
    RecordedSessionIdentityState Identity,
    RecordedSessionSelectionState Selection,
    RecordedSessionTimelineState Timeline,
    RecordedSessionAnalysisState Analysis);

public sealed record RecordedSessionIdentityState(
    Guid SessionId,
    string? Name,
    long? Timestamp,
    double? DurationSeconds,
    bool IsLoaded,
    bool IsActive);

public sealed record RecordedSessionSelectionState(
    TelemetryTimeRange? AnalysisRange);

public sealed record RecordedSessionTimelineState
{
    public RecordedSessionTimelineState(
        TrackTimeRange? trackTimelineContext,
        double? telemetryDurationSeconds,
        IRecordedSessionTimeline? timeline,
        RecordedSessionTimelineAlignmentState? alignment = null)
    {
        TrackTimelineContext = trackTimelineContext;
        TelemetryDurationSeconds = telemetryDurationSeconds;
        Timeline = timeline;
        Alignment = alignment ?? RecordedSessionTimelineAlignmentState.Empty;
    }

    public TrackTimeRange? TrackTimelineContext { get; init; }
    public double? TelemetryDurationSeconds { get; init; }
    public IRecordedSessionTimeline? Timeline { get; init; }
    public RecordedSessionTimelineAlignmentState Alignment { get; init; }
}

public sealed record RecordedSessionAnalysisState(
    SessionDampingPercentages DampingPercentages,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    VelocityAverageMode VelocityAverageMode,
    TravelDistributionMode TravelDistributionMode);

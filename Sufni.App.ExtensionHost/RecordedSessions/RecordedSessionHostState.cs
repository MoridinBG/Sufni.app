using System;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public sealed record RecordedSessionHostState(
    RecordedSessionIdentityState Identity,
    RecordedSessionSelectionState Selection,
    RecordedSessionTimelineState Timeline,
    RecordedSessionStatisticsState Statistics);

public sealed record RecordedSessionIdentityState(
    Guid SessionId,
    string? Name,
    long? Timestamp,
    double? DurationSeconds,
    bool IsLoaded,
    bool IsActive);

public sealed record RecordedSessionSelectionState(
    TelemetryTimeRange? AnalysisRange);

public sealed record RecordedSessionTimelineState(
    TrackTimeRange? TrackTimelineContext,
    double? TelemetryDurationSeconds,
    IRecordedSessionTimeline? Timeline);

public sealed record RecordedSessionStatisticsState(
    SessionDamperPercentages DamperPercentages,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    VelocityAverageMode VelocityAverageMode,
    TravelHistogramMode TravelHistogramMode);

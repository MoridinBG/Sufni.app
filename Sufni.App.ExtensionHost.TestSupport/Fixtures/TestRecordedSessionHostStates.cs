using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.TestSupport.Fixtures;

public static class TestRecordedSessionHostStates
{
    public static RecordedSessionHostState Unloaded(Guid? sessionId = null) =>
        Create(isLoaded: false, sessionId: sessionId, durationSeconds: null);

    public static RecordedSessionHostState LoadedWithoutTelemetry(Guid? sessionId = null) =>
        Create(isLoaded: true, sessionId: sessionId, durationSeconds: null);

    public static RecordedSessionHostState LoadedWithRange(
        Guid? sessionId = null,
        TelemetryTimeRange? analysisRange = null,
        double durationSeconds = 30) =>
        Create(
            isLoaded: true,
            sessionId: sessionId,
            durationSeconds: durationSeconds,
            analysisRange: analysisRange ?? new TelemetryTimeRange(0, Math.Min(1, durationSeconds)));

    public static RecordedSessionHostState LoadedWithTimeline(
        Guid? sessionId = null,
        TrackTimeRange? trackTimelineContext = null,
        IRecordedSessionTimeline? timeline = null,
        double durationSeconds = 30,
        RecordedSessionTimelineAlignmentState? alignment = null) =>
        Create(
            isLoaded: true,
            sessionId: sessionId,
            durationSeconds: durationSeconds,
            trackTimelineContext: trackTimelineContext ?? new TrackTimeRange(0, durationSeconds),
            timeline: timeline,
            alignment: alignment);

    public static RecordedSessionHostState LoadedWithAnalysisState(
        Guid? sessionId = null,
        TelemetryTimeRange? analysisRange = null,
        SessionDampingPercentages? dampingPercentages = null,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null,
        VelocityAverageMode velocityAverageMode = VelocityAverageMode.SampleAveraged,
        TravelDistributionMode travelDistributionMode = TravelDistributionMode.ActiveSuspension,
        double durationSeconds = 30) =>
        Create(
            isLoaded: true,
            sessionId: sessionId,
            durationSeconds: durationSeconds,
            analysisRange: analysisRange,
            dampingPercentages: dampingPercentages,
            dampingSpeedCutoffs: dampingSpeedCutoffs,
            velocityAverageMode: velocityAverageMode,
            travelDistributionMode: travelDistributionMode);

    public static RecordedSessionHostState Create(
        bool isLoaded = true,
        Guid? sessionId = null,
        string? name = "Ride",
        long? timestamp = 1000,
        double? durationSeconds = 30,
        bool isActive = true,
        TelemetryTimeRange? analysisRange = null,
        TrackTimeRange? trackTimelineContext = null,
        IRecordedSessionTimeline? timeline = null,
        RecordedSessionTimelineAlignmentState? alignment = null,
        SessionDampingPercentages? dampingPercentages = null,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null,
        VelocityAverageMode velocityAverageMode = VelocityAverageMode.SampleAveraged,
        TravelDistributionMode travelDistributionMode = TravelDistributionMode.ActiveSuspension) =>
        new(
            new RecordedSessionIdentityState(
                sessionId ?? Guid.NewGuid(),
                name,
                timestamp,
                durationSeconds,
                isLoaded,
                isActive),
            new RecordedSessionSelectionState(analysisRange),
            new RecordedSessionTimelineState(trackTimelineContext, durationSeconds, timeline, alignment),
            new RecordedSessionAnalysisState(
                dampingPercentages ?? SessionDampingPercentages.Empty,
                dampingSpeedCutoffs ?? DampingSpeedCutoffs.Default,
                velocityAverageMode,
                travelDistributionMode));
}

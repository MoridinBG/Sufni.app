using System;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal static class RecordedSessionHostStateProjection
{
    public static RecordedSessionHostState Create(
        RecordedSessionEditorState state,
        RecordedSessionHostRuntimeState runtime,
        IRecordedSessionTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(timeline);

        var timelineDurationSeconds = state.TelemetryData?.Metadata.Duration ?? state.Session?.DurationSeconds;

        return new RecordedSessionHostState(
            new RecordedSessionIdentityState(
                runtime.SessionId,
                state.Session?.Name,
                state.Session?.Timestamp,
                state.Session?.DurationSeconds,
                runtime.ViewLoaded,
                runtime.IsActive),
            new RecordedSessionSelectionState(state.Intent.AnalysisRange),
            new RecordedSessionTimelineState(
                state.TrackTimelineContext,
                timelineDurationSeconds,
                timeline,
                new RecordedSessionTimelineAlignmentState(runtime.PendingTimelineAlignmentMark)),
            new RecordedSessionAnalysisState(
                state.Presentation.DampingPercentages,
                state.Intent.DampingSpeedCutoffs,
                state.Intent.SelectedVelocityAverageMode,
                state.Intent.SelectedTravelDistributionMode));
    }
}

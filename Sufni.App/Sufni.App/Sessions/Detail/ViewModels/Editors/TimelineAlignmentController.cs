using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Video.Coordinators;
using Sufni.App.Video.ViewModels;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class TimelineAlignmentController : IDisposable
{
    private readonly Guid sessionId;
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly VideoPaneViewModel? videoPane;
    private readonly CancellationToken lifetime;
    private readonly Action<FailurePresentation> reportError;
    private readonly Action<string> notify;
    private readonly IDisposable stateSubscription;
    private readonly CancellationTokenRegistration cancellation;
    private RecordedSessionEditorState currentState = RecordedSessionEditorState.CreateInitial();
    private TimelineAlignmentMark? pendingTimelineAlignmentMark;
    public IRelayCommand<TelemetryPlotContextMenuContext?> MarkGpsEventCommand { get; }
    public IAsyncRelayCommand<TelemetryPlotContextMenuContext?> MarkGpsTelemetryEventCommand { get; }
    public IRelayCommand<TelemetryPlotContextMenuContext?> CancelGpsTimelineAlignmentCommand { get; }
    public IRelayCommand<TelemetryPlotContextMenuContext?> MarkVideoEventCommand { get; }
    public IAsyncRelayCommand<TelemetryPlotContextMenuContext?> MarkVideoTelemetryEventCommand { get; }
    public IRelayCommand<TelemetryPlotContextMenuContext?> CancelVideoAlignmentCommand { get; }

    public TimelineAlignmentController(Guid sessionId, IObservable<RecordedSessionEditorState> state,
        ISessionCoordinator sessionCoordinator, VideoPaneViewModel? videoPane, CancellationToken lifetime,
        Action<FailurePresentation> reportError, Action<string> notify)
    {
        this.sessionId = sessionId;
        this.sessionCoordinator = sessionCoordinator;
        this.videoPane = videoPane;
        this.lifetime = lifetime;
        this.reportError = reportError;
        this.notify = notify;
        MarkGpsEventCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            MarkGpsEventFromPlotContext,
            CanMarkGpsEventFromPlotContext);
        MarkGpsTelemetryEventCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            MarkGpsTelemetryEventFromPlotContextAsync,
            CanMarkGpsTelemetryEventFromPlotContext);
        CancelGpsTimelineAlignmentCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            CancelGpsTimelineAlignmentFromPlotContext,
            CanCancelGpsTimelineAlignmentFromPlotContext);
        MarkVideoEventCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            MarkVideoEventFromPlotContext,
            CanMarkVideoEventFromPlotContext);
        MarkVideoTelemetryEventCommand = new AsyncRelayCommand<TelemetryPlotContextMenuContext?>(
            MarkVideoTelemetryEventFromPlotContextAsync,
            CanMarkVideoTelemetryEventFromPlotContext);
        CancelVideoAlignmentCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            CancelVideoAlignmentFromPlotContext,
            CanCancelVideoAlignmentFromPlotContext);
        stateSubscription = state.Subscribe(value =>
        {
            currentState = value;
            NotifyTimelineAlignmentCommandsCanExecuteChanged();
        });
        cancellation = lifetime.Register(() => pendingTimelineAlignmentMark = null);
    }

    public void Dispose()
    {
        pendingTimelineAlignmentMark = null;
        cancellation.Dispose();
        stateSubscription.Dispose();
    }

    private void SetPendingTimelineAlignmentMark(TimelineAlignmentMark? mark)
    {
        pendingTimelineAlignmentMark = mark;
        NotifyTimelineAlignmentCommandsCanExecuteChanged();
    }

    private void NotifyTimelineAlignmentCommandsCanExecuteChanged()
    {
        MarkGpsEventCommand.NotifyCanExecuteChanged();
        MarkGpsTelemetryEventCommand.NotifyCanExecuteChanged();
        CancelGpsTimelineAlignmentCommand.NotifyCanExecuteChanged();
        MarkVideoEventCommand.NotifyCanExecuteChanged();
        MarkVideoTelemetryEventCommand.NotifyCanExecuteChanged();
        CancelVideoAlignmentCommand.NotifyCanExecuteChanged();
    }

    private bool CanMarkGpsEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return pendingTimelineAlignmentMark is null &&
               currentState.TelemetryData is not null &&
               currentState.TrackPoints is { Count: > 0 } &&
               (!lifetime.IsCancellationRequested && SignalPlotContextActions.IsTelemetryPlotContext(context));
    }

    private void MarkGpsEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanMarkGpsEventFromPlotContext(context))
        {
            return;
        }

        SetPendingTimelineAlignmentMark(new TimelineAlignmentMark.GpsTrack(context!.ClickSeconds));
    }

    private bool CanMarkGpsTelemetryEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return currentState.TelemetryData is not null &&
               pendingTimelineAlignmentMark is TimelineAlignmentMark.GpsTrack &&
               (!lifetime.IsCancellationRequested && SignalPlotContextActions.IsTelemetryPlotContext(context));
    }

    private async Task MarkGpsTelemetryEventFromPlotContextAsync(TelemetryPlotContextMenuContext? context)
    {
        if (!CanMarkGpsTelemetryEventFromPlotContext(context) ||
            pendingTimelineAlignmentMark is not TimelineAlignmentMark.GpsTrack mark ||
            currentState.TelemetryData is null)
        {
            return;
        }

        SetPendingTimelineAlignmentMark(null);
        var offsetDeltaSeconds = mark.Seconds - context!.ClickSeconds;
        var applied = await sessionCoordinator.ShiftGpsOffsetAsync(sessionId, offsetDeltaSeconds);

        if (lifetime.IsCancellationRequested) return;
        switch (applied)
        {
            case GpsOffsetResult.TelemetryUnavailable:
                notify("GPS alignment could not be applied because session telemetry is unavailable. Sync or recompute the session and try again.");
                break;
            case GpsOffsetResult.NoMatchingTrack:
                notify("GPS offset could not be applied: no matching track segment was found.");
                break;
            case GpsOffsetResult.SessionMissing:
                notify("The session is no longer available.");
                break;
            case GpsOffsetResult.Failed failed:
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }

        // One-way: on success the store upsert drives the refreshed track and
        // baseline through the session-detail watch reaction, like recompute.
    }

    private bool CanCancelGpsTimelineAlignmentFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return pendingTimelineAlignmentMark is TimelineAlignmentMark.GpsTrack &&
               (!lifetime.IsCancellationRequested && SignalPlotContextActions.IsTelemetryPlotContext(context));
    }

    private void CancelGpsTimelineAlignmentFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanCancelGpsTimelineAlignmentFromPlotContext(context))
        {
            return;
        }

        SetPendingTimelineAlignmentMark(null);
    }

    private bool CanMarkVideoEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return pendingTimelineAlignmentMark is null &&
               (!lifetime.IsCancellationRequested && SignalPlotContextActions.IsTelemetryPlotContext(context)) &&
               videoPane?.ClipAt(context!.ClickSeconds) is not null;
    }

    private void MarkVideoEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanMarkVideoEventFromPlotContext(context) ||
            videoPane?.ClipAt(context!.ClickSeconds) is not { } clip)
        {
            return;
        }

        SetPendingTimelineAlignmentMark(new TimelineAlignmentMark.VideoClip(clip.Id, context.ClickSeconds));
    }

    private bool CanMarkVideoTelemetryEventFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return pendingTimelineAlignmentMark is TimelineAlignmentMark.VideoClip mark &&
               videoPane?.HasPresentClip(mark.ClipId) == true &&
               (!lifetime.IsCancellationRequested && SignalPlotContextActions.IsTelemetryPlotContext(context));
    }

    private async Task MarkVideoTelemetryEventFromPlotContextAsync(TelemetryPlotContextMenuContext? context)
    {
        if (!CanMarkVideoTelemetryEventFromPlotContext(context) ||
            pendingTimelineAlignmentMark is not TimelineAlignmentMark.VideoClip mark)
        {
            return;
        }

        SetPendingTimelineAlignmentMark(null);
        var result = await videoPane!.ShiftAlignmentAsync(mark.ClipId, mark.Seconds - context!.ClickSeconds);
        if (lifetime.IsCancellationRequested) return;
        switch (result)
        {
            case VideoAlignmentResult.ClipMissing:
                notify("Session video alignment could not be saved because the video metadata is missing.");
                break;
            case VideoAlignmentResult.Failed failed:
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    private bool CanCancelVideoAlignmentFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        return pendingTimelineAlignmentMark is TimelineAlignmentMark.VideoClip &&
               (!lifetime.IsCancellationRequested && SignalPlotContextActions.IsTelemetryPlotContext(context));
    }

    private void CancelVideoAlignmentFromPlotContext(TelemetryPlotContextMenuContext? context)
    {
        if (!CanCancelVideoAlignmentFromPlotContext(context))
        {
            return;
        }

        SetPendingTimelineAlignmentMark(null);
    }

}

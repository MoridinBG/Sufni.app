using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;
using Sufni.App.Terrain.Services;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class GpsOffsetSuggestionPrompt : IAsyncDisposable
{
    private readonly ISessionCoordinator sessions;
    private readonly IDialogService dialogs;
    private readonly IBackgroundTaskRunner worker;
    private readonly IUiThreadDispatcher ui;
    private readonly Func<bool> active;
    private readonly Action<FailurePresentation> report;
    private readonly CancellationToken lifetime;
    private readonly IDisposable subscription;
    private readonly CancellableOperation calculation = new();
    private TaskCompletionSource? activation;
    private SessionContentToken? current;
    private Task pending = Task.CompletedTask;

    public GpsOffsetSuggestionPrompt(IObservable<RecordedSessionEditorState> states, ISessionCoordinator sessions,
        IDialogService dialogs, IBackgroundTaskRunner worker, IUiThreadDispatcher ui, Func<bool> active,
        Action<FailurePresentation> report, CancellationToken lifetime)
    {
        this.sessions = sessions;
        this.dialogs = dialogs;
        this.worker = worker;
        this.ui = ui;
        this.active = active;
        this.report = report;
        this.lifetime = lifetime;
        subscription = states.Subscribe(Update);
    }

    public void Activate() => activation?.TrySetResult();

    private void Update(RecordedSessionEditorState state)
    {
        if (lifetime.IsCancellationRequested) return;
        if (state.Load is not RecordedSessionLoadPresentation.Loaded loaded ||
            loaded.Data.TelemetryPresentation.FullTrackId is not { } trackId ||
            state.Session is not { } session || state.TelemetryData is not { ImuData: { } imu } telemetry ||
            state.FullTrackPoints is not { Count: > 0 } fixes || state.TrackTimeline is not { } timeline ||
            !imu.ActiveLocations.Contains((byte)ImuLocation.Frame) || state.Preferences.LastGpsOffsetTrackId == trackId ||
            (state.Domain?.SelectedTrack is { } selected && selected.Id != trackId))
        {
            if (current.HasValue) calculation.Cancel();
            current = null;
            return;
        }
        var key = new SessionContentToken(session.ProcessedTelemetryRevision, trackId, state.Domain?.SelectedTrack?.PointsRevision,
            telemetry.Metadata.Timestamp, telemetry.Metadata.Duration, timeline.GpsOffsetSeconds,
            WindowOriginFraction: SessionTrackTimeline.FractionalOrigin(timeline.OwnerOriginSeconds));
        if (key == current) return;
        current = key;
        var token = calculation.Start(lifetime);
        pending = RunAsync(pending, session.Id, trackId, key, () => GpsTimeOffsetEstimator.Estimate(telemetry,
            TrackSampleAdapter.FromOriginalFixes(fixes, timeline, telemetry.Metadata.Duration), token), token);
    }

    private async Task RunAsync(Task previous, Guid sessionId, Guid trackId, SessionContentToken key,
        Func<GpsTimeOffsetEstimate> estimate, CancellationToken token)
    {
        try
        {
            await Task.Yield();
            await previous;
            token.ThrowIfCancellationRequested();
            var measured = await worker.RunAsync(estimate, token);
            if (measured is not GpsTimeOffsetEstimate.Measured { ShouldSuggest: true } suggestion) return;
            await ui.InvokeAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                while (!active())
                {
                    activation = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    try { await activation.Task.WaitAsync(token); }
                    finally { activation = null; }
                }
                token.ThrowIfCancellationRequested();
                var choice = await dialogs.ShowChoiceAsync("GPS time offset",
                    $"The current GPS offset is {key.GpsOffsetSeconds:F1} s. Aligning the track to the frame gyroscope suggests {key.GpsOffsetSeconds + suggestion.LagSeconds:F1} s.",
                    [new("apply", "Apply", IsDefault: true), new("later", "Not now")]).WaitAsync(lifetime);
                token.ThrowIfCancellationRequested();
                if (choice == "apply")
                {
                    // Applying changes the content token itself.
                    var result = await sessions.ShiftGpsOffsetAsync(sessionId, suggestion.LagSeconds, lifetime);
                    lifetime.ThrowIfCancellationRequested();
                    switch (result)
                    {
                        case GpsOffsetResult.NoMatchingTrack: report(new("No matching GPS track is available.")); break;
                        case GpsOffsetResult.TelemetryUnavailable: report(new("Session telemetry is unavailable.")); break;
                        case GpsOffsetResult.SessionMissing: report(new("The session is no longer available.")); break;
                        case GpsOffsetResult.Failed failed: report(FailurePresentation.From(failed.Reason, failed.Details)); break;
                    }
                }
                var saved = await sessions.SavePreferencesAsync(sessionId, p => p with { LastGpsOffsetTrackId = trackId });
                if (!lifetime.IsCancellationRequested && saved is SessionPreferenceSaveResult.Failed failure)
                    report(FailurePresentation.From(failure.Reason, failure.Details));
            });
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
                await ui.InvokeAsync(() => report(FailurePresentation.From(SessionEditorFailureReason.Analysis, ex.Message)));
        }
    }

    public async ValueTask DisposeAsync()
    {
        subscription.Dispose();
        calculation.Dispose();
        await pending;
    }
}

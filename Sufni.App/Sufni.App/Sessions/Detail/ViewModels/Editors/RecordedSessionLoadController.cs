using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Models;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class RecordedSessionLoadController : IDisposable
{
    private readonly Guid sessionId;
    private readonly ISessionLoader sessionLoader;
    private readonly ISessionStore sessionStore;
    private readonly ISessionProcessedTelemetryReader processedTelemetryReader;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly CancellationToken lifetime;
    private readonly Action<RecordedSessionLoadPresentation> publish;
    private readonly Action<FailurePresentation> reportError;
    private readonly CancellableOperation loadOperation = new();
    private readonly IDisposable stateSubscription;
    private IDisposable? processedTelemetryRetention;
    private RecordedSessionEditorState currentState = RecordedSessionEditorState.CreateInitial();

    public RecordedSessionLoadController(Guid sessionId, IObservable<RecordedSessionEditorState> state,
        ISessionLoader sessionLoader, ISessionStore sessionStore, ISessionProcessedTelemetryReader processedTelemetryReader,
        IUiThreadDispatcher uiThreadDispatcher,
        CancellationToken lifetime, Action<RecordedSessionLoadPresentation> publish, Action<FailurePresentation> reportError)
    {
        this.sessionId = sessionId;
        this.sessionLoader = sessionLoader;
        this.sessionStore = sessionStore;
        this.processedTelemetryReader = processedTelemetryReader;
        this.uiThreadDispatcher = uiThreadDispatcher;
        this.lifetime = lifetime;
        this.publish = publish;
        this.reportError = reportError;
        stateSubscription = state.Subscribe(value => currentState = value);
    }

    public void Start() => processedTelemetryRetention ??= processedTelemetryReader.Retain(sessionId);

    public void Stop()
    {
        loadOperation.Cancel();
        processedTelemetryRetention?.Dispose();
        processedTelemetryRetention = null;
    }

    public void Dispose()
    {
        Stop();
        loadOperation.Dispose();
        stateSubscription.Dispose();
    }

    public async Task RequestLoadAsync()
    {
        if (lifetime.IsCancellationRequested)
        {
            return;
        }

        var token = loadOperation.Start(lifetime);
        var currentSnapshot = sessionStore.Get(sessionId);
        var mapExpected = currentState.Domain?.SelectedTrack is not null;
        if (currentSnapshot?.HasProcessedData == true)
        {
            await PublishSessionLoadProgressAsync(
                SessionDetailLoadProgress.PreparingSession,
                mapExpected,
                currentSnapshot,
                token);
        }
        else
        {
            PublishLoadPresentation(new RecordedSessionLoadPresentation.Empty(currentSnapshot));
        }

        try
        {
            var progress = new SessionLoadProgressReporter(
                update => PublishSessionLoadProgress(update, mapExpected, currentSnapshot, token));
            var result = await sessionLoader.LoadDetailAsync(sessionId, progress, token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            await PublishSessionLoadProgressAsync(
                SessionDetailLoadProgress.ApplyingSessionData,
                mapExpected,
                sessionStore.Get(sessionId) ?? currentSnapshot,
                token);
            if (token.IsCancellationRequested) return;
            var loadPresentation = CreateLoadPresentation(result, sessionStore.Get(sessionId) ?? currentSnapshot);
            PublishLoadPresentation(loadPresentation);
            if (loadPresentation is RecordedSessionLoadPresentation.Failed failed)
                reportError(failed.Failure);

            if (result is not SessionDetailLoadResult.Loaded loaded)
            {
                return;
            }

            var trackResult = await sessionLoader.LoadTrackAsync(
                sessionId,
                loaded.Data.TelemetryPresentation.TelemetryData,
                progress,
                token);
            if (token.IsCancellationRequested)
            {
                return;
            }

            var settledResult = CreateSettledLoadResult(loaded, trackResult);
            await PublishSessionLoadProgressAsync(
                SessionDetailLoadProgress.ApplyingSessionData,
                mapExpected,
                sessionStore.Get(sessionId) ?? currentSnapshot,
                token);
            if (token.IsCancellationRequested) return;
            PublishLoadPresentation(CreateLoadPresentation(
                settledResult,
                sessionStore.Get(sessionId) ?? currentSnapshot));
            if (settledResult.Data.TrackFailure is { } failure)
                reportError(FailurePresentation.From(failure.Reason, failure.Details));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private void PublishLoadPresentation(RecordedSessionLoadPresentation presentation)
    {
        if (currentState.Load != presentation)
        {
            publish(presentation);
        }
    }

    private void PublishSessionLoadProgress(
        SessionDetailLoadProgress progress,
        bool mapExpected,
        SessionSnapshot? fallbackSnapshot,
        CancellationToken token)
    {
        if (token.IsCancellationRequested || lifetime.IsCancellationRequested)
        {
            return;
        }

        if (uiThreadDispatcher.CheckAccess())
        {
            PublishSessionLoadProgressOnCurrentThread(progress, mapExpected, fallbackSnapshot, token);
            return;
        }

        uiThreadDispatcher.Post(() =>
            PublishSessionLoadProgressOnCurrentThread(progress, mapExpected, fallbackSnapshot, token));
    }

    private Task PublishSessionLoadProgressAsync(
        SessionDetailLoadProgress progress,
        bool mapExpected,
        SessionSnapshot? fallbackSnapshot,
        CancellationToken token)
    {
        if (token.IsCancellationRequested || lifetime.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        if (uiThreadDispatcher.CheckAccess())
        {
            PublishSessionLoadProgressOnCurrentThread(progress, mapExpected, fallbackSnapshot, token);
            return Task.CompletedTask;
        }

        return uiThreadDispatcher.InvokeAsync(() =>
            PublishSessionLoadProgressOnCurrentThread(progress, mapExpected, fallbackSnapshot, token));
    }

    private void PublishSessionLoadProgressOnCurrentThread(
        SessionDetailLoadProgress progress,
        bool mapExpected,
        SessionSnapshot? fallbackSnapshot,
        CancellationToken token)
    {
        if (token.IsCancellationRequested ||
            lifetime.IsCancellationRequested ||
            currentState.Load is RecordedSessionLoadPresentation.Loaded)
        {
            return;
        }

        PublishLoadPresentation(new RecordedSessionLoadPresentation.Loading(
            mapExpected,
            progress,
            currentState.Session ?? fallbackSnapshot));
    }

    private sealed class SessionLoadProgressReporter : IProgress<SessionDetailLoadProgress>
    {
        private readonly Action<SessionDetailLoadProgress> onReport;

        public SessionLoadProgressReporter(Action<SessionDetailLoadProgress> onReport)
        {
            this.onReport = onReport;
        }

        public void Report(SessionDetailLoadProgress value)
        {
            onReport(value);
        }
    }

    private static SessionDetailLoadResult.Loaded CreateSettledLoadResult(
        SessionDetailLoadResult.Loaded initialResult,
        SessionDetailTrackLoadResult trackResult)
    {
        return trackResult switch
        {
            SessionDetailTrackLoadResult.Loaded loaded =>
                new SessionDetailLoadResult.Loaded(
                    new SessionDetailData(
                        initialResult.Data.TelemetryPresentation with
                        {
                            FullTrackId = loaded.Data.FullTrackId,
                            FullTrackPoints = loaded.Data.FullTrackPoints,
                            TrackPoints = loaded.Data.TrackPoints,
                        })),
            SessionDetailTrackLoadResult.Failed failed =>
                new SessionDetailLoadResult.Loaded(initialResult.Data with { TrackFailure = failed }),
            _ => throw new InvalidOperationException("Unsupported session track load result."),
        };
    }

    private static RecordedSessionLoadPresentation CreateLoadPresentation(
        SessionDetailLoadResult result,
        SessionSnapshot? snapshot)
    {
        return result switch
        {
            SessionDetailLoadResult.Loaded loaded => new RecordedSessionLoadPresentation.Loaded(
                loaded.Data,
                snapshot),
            SessionDetailLoadResult.IncompleteLocalData incomplete =>
                new RecordedSessionLoadPresentation.IncompleteLocalData(
                    incomplete.Missing,
                    snapshot),
            SessionDetailLoadResult.Failed failed => new RecordedSessionLoadPresentation.Failed(
                FailurePresentation.From(failed.Reason, failed.Details),
                snapshot),
            _ => new RecordedSessionLoadPresentation.Empty(snapshot),
        };
    }

}

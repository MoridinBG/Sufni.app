using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services.Preferences;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Models;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed class RecordedSessionPreferencesController : IDisposable
{
    private readonly Guid sessionId;
    private readonly ISessionStore sessionStore;
    private readonly ISessionPreferences sessionPreferences;
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly RecordedSessionEditorActions actions;
    private readonly CancellationToken lifetime;
    private readonly Action<SessionPreferences> publish;
    private readonly Action<FailurePresentation> reportError;
    private readonly Action<string> notify;
    private readonly CompositeDisposable subscriptions;
    private readonly Queue<Func<SessionPreferences, SessionPreferences>> pendingEdits = new();
    private SessionPreferences persisted = SessionPreferences.Default;
    private RecordedSessionEditorState currentState = RecordedSessionEditorState.CreateInitial();
    private RecordedProcessingDraft? submittedProcessingDraft;
    private IDisposable? observation;
    private bool hasObservedPreferences;
    private bool savingPreferences;
    private Task pendingOperations = Task.CompletedTask;

    public RecordedSessionPreferencesController(Guid sessionId, IObservable<RecordedSessionEditorState> state,
        RecordedSessionEditorActions actions, ISessionStore sessionStore,
        ISessionPreferences sessionPreferences, ISessionCoordinator sessionCoordinator,
        CancellationToken lifetime, Action<SessionPreferences> publish,
        Action<FailurePresentation> reportError, Action<string> notify)
    {
        this.sessionId = sessionId;
        this.sessionStore = sessionStore;
        this.sessionPreferences = sessionPreferences;
        this.sessionCoordinator = sessionCoordinator;
        this.actions = actions;
        this.lifetime = lifetime;
        this.publish = publish;
        this.reportError = reportError;
        this.notify = notify;
        subscriptions = new CompositeDisposable(
            state.Subscribe(value => currentState = value),
            actions.Intents.Subscribe(HandleIntent));
    }

    public Task PendingOperations => pendingOperations;

    public void Start() => observation = sessionPreferences.ObserveRecorded(sessionId).Subscribe(value =>
    {
        // Preferences publish on the UI thread before their write completes. Keep that
        // acknowledgement ordered with the local edits instead of posting it again.
        hasObservedPreferences = true;
        persisted = value;
        PublishEffectivePreferences();
    });

    public void Stop()
    {
        observation?.Dispose();
        observation = null;
        subscriptions.Dispose();
    }

    public void Dispose() => Stop();

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await sessionCoordinator.LoadPreferencesAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        if (lifetime.IsCancellationRequested) return;
        switch (result)
        {
            case SessionPreferenceLoadResult.Loaded loaded when !hasObservedPreferences:
                persisted = loaded.Preferences;
                PublishEffectivePreferences();
                break;
            case SessionPreferenceLoadResult.Failed failed:
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    private void HandleIntent(RecordedSessionEditorIntent intent)
    {
        if (lifetime.IsCancellationRequested) return;
        if (intent is RecordedSessionEditorIntent.CommitProcessingOption)
        {
            pendingOperations = Task.WhenAll(pendingOperations, CommitProcessingOptionAsync());
            return;
        }
        if (RecordedSessionEditorIntentReducers.CreatePreferenceUpdate(intent) is not { } update) return;

        pendingEdits.Enqueue(update);
        var startSaving = !savingPreferences;
        savingPreferences = true;
        PublishEffectivePreferences();
        if (startSaving)
            pendingOperations = Task.WhenAll(pendingOperations, SavePendingPreferencesAsync());
    }

    private void PublishEffectivePreferences()
    {
        if (lifetime.IsCancellationRequested) return;
        var effective = persisted;
        foreach (var update in pendingEdits) effective = update(effective);
        publish(effective);
    }

    private async Task SavePendingPreferencesAsync()
    {
        while (pendingEdits.TryPeek(out var update))
        {
            var result = await sessionCoordinator.SavePreferencesAsync(sessionId, update);
            pendingEdits.Dequeue();
            PublishEffectivePreferences();
            if (!lifetime.IsCancellationRequested && result is SessionPreferenceSaveResult.Failed failed)
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
        }
        savingPreferences = false;
    }

    private async Task CommitProcessingOptionAsync()
    {
        if (observation is null || lifetime.IsCancellationRequested ||
            currentState.ProcessingDraft is not { } draft || ReferenceEquals(submittedProcessingDraft, draft) ||
            sessionStore.Get(sessionId) is not { } previous) return;
        submittedProcessingDraft = draft;
        if (draft.WindowMilliseconds == previous.ProcessingOptions.ClampedVelocityFilterWindowMilliseconds)
        {
            actions.CompleteProcessingOption(draft, previous);
            return;
        }

        var result = await sessionCoordinator.SetProcessingOptionAsync(sessionId, draft.WindowMilliseconds);
        if (lifetime.IsCancellationRequested) return;
        if (result is not SessionRecomputeResult.Superseded)
            actions.CompleteProcessingOption(draft, sessionStore.Get(sessionId) ?? previous);
        switch (result)
        {
            case SessionRecomputeResult.ProcessingFailed failed:
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SessionRecomputeResult.Failed failed:
                reportError(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case SessionRecomputeResult.Missing:
                notify("The session is no longer available.");
                break;
            case SessionRecomputeResult.NotRecomputable:
                reportError(new FailurePresentation("Session is stale and cannot be recomputed until the source recording is restored."));
                break;
        }
    }
}

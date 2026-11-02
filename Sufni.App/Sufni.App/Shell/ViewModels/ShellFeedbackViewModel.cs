using System;
using System.Reactive.Linq;
using Sufni.App.SyncAndPairing.Models;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Models;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Video.Coordinators;
using Sufni.App.Sessions.Coordination;

namespace Sufni.App.Shell.ViewModels;

public interface IShellActions
{
    IAsyncRelayCommand SyncCommand { get; }
    bool CanImportSessions { get; }
}

/// <summary>Application-wide action availability, progress, and result feedback.</summary>
public sealed class ShellFeedbackViewModel : ViewModelBase, IShellActions, IDisposable
{
    private readonly ISyncCoordinator sync;
    private readonly ISessionCoordinator sessions;
    private readonly IAppEnvironment environment;
    private readonly IDisposable stateSubscription;
    private SyncState state;

    public ShellFeedbackViewModel(ISyncCoordinator sync, ISessionCoordinator sessions, IAppEnvironment environment,
        IUiThreadDispatcher dispatcher, TimeProvider timeProvider) : base(dispatcher, timeProvider)
    {
        this.sync = sync;
        this.sessions = sessions;
        this.environment = environment;
        state = sync.State;
        SyncCommand = new AsyncRelayCommand(() => sync.SyncAllAsync(),
            () => (environment.Capabilities.CanHostSyncServer || environment.Capabilities.CanPairAsClient) && state.CanSync);
        stateSubscription = sync.States.ObserveOnUiThread(dispatcher).Subscribe(value =>
        {
            state = value;
            OnSyncStateChanged();
            SyncCommand.NotifyCanExecuteChanged();
        });
        sync.SyncCompleted += OnSyncCompleted;
        sync.SyncFailed += OnSyncFailed;
        sync.ServerUnavailable += OnServerUnavailable;
        sessions.RecomputeAllFinished += OnRecomputeAllFinished;
    }

    public IAsyncRelayCommand SyncCommand { get; }
    public bool CanImportSessions => environment.Capabilities.SupportsMassStorageImport ||
                                     environment.Capabilities.SupportsStorageProviderImport;
    public bool IsPaired => state.IsPaired;
    public bool SyncInProgress => state.IsRunning;
    public string SyncProgressText => state.Progress?.Message ?? (SyncInProgress ? "Syncing" : string.Empty);
    public double SyncProgressValue => state.Progress?.Fraction ?? 0;
    public bool SyncProgressIsIndeterminate => state.Progress is null || !state.Progress.IsDeterminate;

    private void OnSyncStateChanged()
    {
        OnPropertyChanged(nameof(IsPaired));
        OnPropertyChanged(nameof(SyncInProgress));
        OnPropertyChanged(nameof(SyncProgressText));
        OnPropertyChanged(nameof(SyncProgressValue));
        OnPropertyChanged(nameof(SyncProgressIsIndeterminate));
    }

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs args) => UiThreadDispatcher.Post(() =>
    {
        Notifications.Add(GetCompletionMessage(args.Result));
        ErrorMessages.Clear();
    });

    private static string GetCompletionMessage(SynchronizationRunResult result) =>
        result switch
        {
            SynchronizationRunResult.Completed => "Sync successful",
            SynchronizationRunResult.IncompleteData incomplete when incomplete.MissingProcessedSessionCount == 0
                && incomplete.IncompleteRecordedSourceCount == 0 =>
                $"Sync incomplete: {incomplete.PendingTransfers.Count} transfer(s) pending",
            SynchronizationRunResult.IncompleteData incomplete =>
                $"Sync incomplete: {incomplete.MissingProcessedSessionCount} session blob(s) and {incomplete.IncompleteRecordedSourceCount} recorded source(s) still missing"
                + (incomplete.PendingTransfers.Count == 0 ? "" : $"; {incomplete.PendingTransfers.Count} transfer(s) pending"),
            _ => "Sync completed"
        };

    private void OnSyncFailed(object? sender, SyncFailedEventArgs args) =>
        UiThreadDispatcher.Post(() => ErrorMessages.Add(FailurePresentation.From(args.Reason, args.Details)));

    private void OnServerUnavailable(object? sender, EventArgs args) =>
        UiThreadDispatcher.Post(() => Notifications.Add("No sync server is available. Try again when the server is running."));

    public void ShowGpxResult(GpxImportResult result)
    {
        switch (result)
        {
            case GpxImportResult.Completed completed:
                ShowGpxCounts(completed.ImportedCount, completed.AlreadyImportedCount);
                break;
            case GpxImportResult.Failed failed:
                ShowGpxCounts(failed.ImportedCount, failed.AlreadyImportedCount);
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    private void ShowGpxCounts(int importedCount, int alreadyImportedCount)
    {
        if (importedCount == 0 && alreadyImportedCount == 0) return;
        if (importedCount > 0)
            Notifications.Add(alreadyImportedCount > 0
                ? $"Imported {importedCount} GPX track(s); skipped {alreadyImportedCount} already imported track(s)."
                : $"Imported {importedCount} GPX track(s).");
        else
            Notifications.Add(alreadyImportedCount == 1
                ? "GPX track is already imported."
                : $"{alreadyImportedCount} GPX tracks are already imported.");
    }

    public void ShowVideoResult(VideoImportResult result)
    {
        switch (result)
        {
            case VideoImportResult.Linked linked:
                Notifications.Add(linked.SessionCount == 1 ? "Linked video to 1 recorded session."
                    : $"Linked video to {linked.SessionCount} recorded sessions.");
                break;
            case VideoImportResult.SourceUnavailable:
                Notifications.Add("A matching recording source is not available yet. Sync it, then import the video again.");
                break;
            case VideoImportResult.NoMatchingSession:
                Notifications.Add("No recorded session overlaps the selected video.");
                break;
            case VideoImportResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    public void ShowUnexpectedFailure() => ErrorMessages.Add(new FailurePresentation("The action could not be completed. See the log for details."));

    private void OnRecomputeAllFinished(object? sender, SessionRecomputeAllOutcome outcome) => UiThreadDispatcher.Post(() =>
    {
        switch (outcome)
        {
            case SessionRecomputeAllOutcome.Completed completed when completed.Result.Failed > 0:
                ErrorMessages.Add(new FailurePresentation($"{completed.Result.Failed} of {completed.Result.Total} sessions could not be recomputed."));
                break;
            case SessionRecomputeAllOutcome.Completed completed:
                Notifications.Add($"Recomputed {completed.Result.Recomputed} session(s).");
                break;
            case SessionRecomputeAllOutcome.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    });

    public void Dispose()
    {
        stateSubscription.Dispose();
        sync.SyncCompleted -= OnSyncCompleted;
        sync.SyncFailed -= OnSyncFailed;
        sync.ServerUnavailable -= OnServerUnavailable;
        sessions.RecomputeAllFinished -= OnRecomputeAllFinished;
        ClearNotificationsCommand.Execute(null);
    }
}

using System;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Models;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.Video.Coordinators;

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
    private readonly IAppEnvironment environment;

    public ShellFeedbackViewModel(ISyncCoordinator sync, IAppEnvironment environment,
        IUiThreadDispatcher dispatcher, TimeProvider timeProvider) : base(dispatcher, timeProvider)
    {
        this.sync = sync;
        this.environment = environment;
        SyncCommand = new AsyncRelayCommand(() => sync.SyncAllAsync(),
            () => (environment.Capabilities.CanHostSyncServer || environment.Capabilities.CanPairAsClient) && sync.CanSync);
        sync.IsRunningChanged += OnSyncStateChanged;
        sync.IsPairedChanged += OnSyncStateChanged;
        sync.ProgressChanged += OnSyncStateChanged;
        sync.CanSyncChanged += OnCanSyncChanged;
        sync.SyncCompleted += OnSyncCompleted;
        sync.SyncFailed += OnSyncFailed;
        sync.ServerUnavailable += OnServerUnavailable;
    }

    public IAsyncRelayCommand SyncCommand { get; }
    public bool CanImportSessions => environment.Capabilities.SupportsMassStorageImport ||
                                     environment.Capabilities.SupportsStorageProviderImport;
    public bool IsPaired => sync.IsPaired;
    public bool SyncInProgress => sync.IsRunning;
    public string SyncProgressText => sync.Progress?.Message ?? (SyncInProgress ? "Syncing" : string.Empty);
    public double SyncProgressValue => sync.Progress?.Fraction ?? 0;
    public bool SyncProgressIsIndeterminate => sync.Progress is null || !sync.Progress.IsDeterminate;

    private void OnSyncStateChanged(object? sender, EventArgs args) => UiThreadDispatcher.Post(() =>
    {
        OnPropertyChanged(nameof(IsPaired));
        OnPropertyChanged(nameof(SyncInProgress));
        OnPropertyChanged(nameof(SyncProgressText));
        OnPropertyChanged(nameof(SyncProgressValue));
        OnPropertyChanged(nameof(SyncProgressIsIndeterminate));
    });

    private void OnCanSyncChanged(object? sender, EventArgs args) =>
        UiThreadDispatcher.Post(SyncCommand.NotifyCanExecuteChanged);

    private void OnSyncCompleted(object? sender, SyncCompletedEventArgs args) => UiThreadDispatcher.Post(() =>
    {
        Notifications.Add(args.Message);
        ErrorMessages.Clear();
    });

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

    public void Dispose()
    {
        sync.IsRunningChanged -= OnSyncStateChanged;
        sync.IsPairedChanged -= OnSyncStateChanged;
        sync.ProgressChanged -= OnSyncStateChanged;
        sync.CanSyncChanged -= OnCanSyncChanged;
        sync.SyncCompleted -= OnSyncCompleted;
        sync.SyncFailed -= OnSyncFailed;
        sync.ServerUnavailable -= OnServerUnavailable;
        ClearNotificationsCommand.Execute(null);
    }
}

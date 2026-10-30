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
        UiThreadDispatcher.Post(() => ErrorMessages.Add(args.ErrorMessage));

    public void ShowGpxResult(GpxImportResult result)
    {
        if (result.ImportedCount == 0 && result.AlreadyImportedCount == 0) return;
        if (result.ImportedCount > 0)
            Notifications.Add(result.AlreadyImportedCount > 0
                ? $"Imported {result.ImportedCount} GPX track(s); skipped {result.AlreadyImportedCount} already imported track(s)."
                : $"Imported {result.ImportedCount} GPX track(s).");
        else
            Notifications.Add(result.AlreadyImportedCount == 1
                ? "GPX track is already imported."
                : $"{result.AlreadyImportedCount} GPX tracks are already imported.");
    }

    public void ShowVideoResult(VideoImportResult result)
    {
        switch (result)
        {
            case VideoImportResult.Linked linked:
                Notifications.Add(linked.SessionCount == 1 ? "Linked video to 1 recorded session."
                    : $"Linked video to {linked.SessionCount} recorded sessions.");
                break;
            case VideoImportResult.NoMatchingSession:
                Notifications.Add("No recorded session overlaps the selected video.");
                break;
            case VideoImportResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details).Summary);
                break;
        }
    }

    public void ShowUnexpectedFailure() => ErrorMessages.Add("The action could not be completed. See the log for details.");

    public void Dispose()
    {
        sync.IsRunningChanged -= OnSyncStateChanged;
        sync.IsPairedChanged -= OnSyncStateChanged;
        sync.ProgressChanged -= OnSyncStateChanged;
        sync.CanSyncChanged -= OnCanSyncChanged;
        sync.SyncCompleted -= OnSyncCompleted;
        sync.SyncFailed -= OnSyncFailed;
        ClearNotificationsCommand.Execute(null);
    }
}

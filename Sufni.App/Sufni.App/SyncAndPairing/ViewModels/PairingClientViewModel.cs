using Sufni.App.Shared.Models;
using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.SyncAndPairing.ViewModels;

public partial class PairingClientViewModel : TabPageViewModelBase, IDisposable
{
    #region Observable properties

    [ObservableProperty] public partial string? ServerUrl { get; set; }
    [ObservableProperty] public partial string? DisplayName { get; set; }
    [ObservableProperty] public partial string? Pin { get; set; }
    [ObservableProperty] public partial SyncState SyncState { get; private set; }

    #endregion

    #region Private members

    private readonly IPairingClientCoordinator coordinator;
    private readonly ISyncCoordinator sync;
    private readonly IDisposable stateSubscription;

    #endregion Private members

    #region Constructors

    public PairingClientViewModel(
        IPairingClientCoordinator coordinator,
        ISyncCoordinator sync,
        IShellCoordinator shell,
        IDialogService dialogService,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.coordinator = coordinator;
        this.sync = sync;
        Name = "Pair";

        DisplayName = coordinator.DisplayName;
        ServerUrl = coordinator.ServerUrl;
        SyncState = sync.State;

        coordinator.DisplayNameChanged += OnDisplayNameChanged;
        coordinator.ServerUrlChanged += OnServerUrlChanged;
        stateSubscription = sync.States.ObserveOnUiThread(uiThreadDispatcher).Subscribe(value => SyncState = value);
    }

    #endregion

    #region Private methods

    private void OnDisplayNameChanged(object? sender, EventArgs e)
    {
        UiThreadDispatcher.Post(() => DisplayName = coordinator.DisplayName);
    }

    private void OnServerUrlChanged(object? sender, EventArgs e)
    {
        UiThreadDispatcher.Post(() => ServerUrl = coordinator.ServerUrl);
    }

    public void Dispose()
    {
        coordinator.DisplayNameChanged -= OnDisplayNameChanged;
        coordinator.ServerUrlChanged -= OnServerUrlChanged;
        stateSubscription.Dispose();
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private async Task RequestPairing()
    {
        var result = await coordinator.RequestPairingAsync(DisplayName);
        switch (result)
        {
            case RequestPairingResult.ServerUnavailable:
                Notifications.Add("No server was found. Make sure the desktop app is running on the same network.");
                break;
            case RequestPairingResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    [RelayCommand]
    private async Task ConfirmPairing()
    {
        Debug.Assert(Pin is not null);

        var result = await sync.ConfirmPairingAndSyncAsync(DisplayName, Pin,
            new UiThreadProgress<PairingClientState>(UiThreadDispatcher, _ =>
            {
                Pin = null;
                ErrorMessages.Clear();
                shell.GoBack();
            }));
        switch (result)
        {
            case ConfirmPairingResult.PinRejected:
                Notifications.Add("The PIN was not accepted. Check the PIN or request a new one.");
                break;
            case ConfirmPairingResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    [RelayCommand]
    private async Task Unpair()
    {
        var result = await coordinator.UnpairAsync();
        switch (result)
        {
            case UnpairResult.Unpaired:
                break;
            case UnpairResult.LocalOnly:
                Notifications.Add("Unpaired on this device. The server could not be notified.");
                break;
            case UnpairResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    [RelayCommand]
    private void Loaded()
    {
        coordinator.StartBrowsing();
    }

    [RelayCommand]
    private void Unloaded()
    {
        coordinator.StopBrowsing();
    }

    #endregion Commands
}

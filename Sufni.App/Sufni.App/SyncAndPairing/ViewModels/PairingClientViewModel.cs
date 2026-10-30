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
namespace Sufni.App.SyncAndPairing.ViewModels;

public partial class PairingClientViewModel : TabPageViewModelBase
{
    #region Observable properties

    [ObservableProperty] public partial string? ServerUrl { get; set; }
    [ObservableProperty] public partial string? DisplayName { get; set; }
    [ObservableProperty] public partial string? Pin { get; set; }
    [ObservableProperty] public partial bool IsRequestSent { get; set; }
    [ObservableProperty] public partial bool IsPaired { get; set; }

    #endregion

    #region Private members

    private readonly IPairingClientCoordinator coordinator;

    #endregion Private members

    #region Constructors

    public PairingClientViewModel(
        IPairingClientCoordinator coordinator,
        IShellCoordinator shell,
        IDialogService dialogService,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.coordinator = coordinator;
        Name = "Pair";

        DisplayName = coordinator.DisplayName;
        ServerUrl = coordinator.ServerUrl;
        IsPaired = coordinator.IsPaired;

        coordinator.DisplayNameChanged += OnDisplayNameChanged;
        coordinator.ServerUrlChanged += OnServerUrlChanged;
        _ = coordinator.PairedState.Subscribe(OnPairedStateChanged);
    }

    #endregion

    #region Private methods

    private void OnDisplayNameChanged(object? sender, EventArgs e)
    {
        UiThreadDispatcher.InvokeAsync(() => DisplayName = coordinator.DisplayName);
    }

    private void OnServerUrlChanged(object? sender, EventArgs e)
    {
        UiThreadDispatcher.InvokeAsync(() => ServerUrl = coordinator.ServerUrl);
    }

    private void OnPairedStateChanged(bool isPaired)
    {
        UiThreadDispatcher.InvokeAsync(() => IsPaired = isPaired);
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private async Task RequestPairing()
    {
        var result = await coordinator.RequestPairingAsync(DisplayName);
        switch (result)
        {
            case RequestPairingResult.Sent:
                IsRequestSent = true;
                break;
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

        var result = await coordinator.ConfirmPairingAsync(DisplayName, Pin);
        switch (result)
        {
            case ConfirmPairingResult.Paired:
                Pin = null;
                IsRequestSent = false;
                ErrorMessages.Clear();
                _ = shell.GoBack();
                break;
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

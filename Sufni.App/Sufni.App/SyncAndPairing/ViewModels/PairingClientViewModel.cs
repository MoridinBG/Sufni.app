using System;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.Services;

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
        IUiThreadDispatcher uiThreadDispatcher)
        : base(shell, dialogService, uiThreadDispatcher)
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
            case RequestPairingResult.Failed failed:
                ErrorMessages.Add($"Could not request pairing: {failed.ErrorMessage}");
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
                break;
            case ConfirmPairingResult.Failed failed:
                ErrorMessages.Add($"Could not pair: {failed.ErrorMessage}");
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
            case UnpairResult.LocalOnly localOnly:
                Notifications.Add($"Could unpair only locally: {localOnly.Reason}");
                break;
            case UnpairResult.Failed failed:
                ErrorMessages.Add($"Could not unpair: {failed.ErrorMessage}");
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

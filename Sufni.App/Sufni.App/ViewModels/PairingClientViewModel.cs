using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;

namespace Sufni.App.ViewModels;

public partial class PairingClientViewModel : ViewModelBase
{
    #region Observable properties

    [ObservableProperty] private string? serverUrl;
    [ObservableProperty] private string? deviceId;
    [ObservableProperty] private string? pin;
    [ObservableProperty] private bool isRequestSent;
    [ObservableProperty] private bool isPaired;

    #endregion

    #region Private members

    private readonly IPairingClientCoordinator coordinator;
    private readonly IShellCoordinator shell;

    #endregion Private members

    #region Constructors

    public PairingClientViewModel()
    {
        coordinator = null!;
        shell = null!;
    }

    public PairingClientViewModel(
        IPairingClientCoordinator coordinator,
        IShellCoordinator shell)
    {
        this.coordinator = coordinator;
        this.shell = shell;

        DeviceId = coordinator.DeviceId;
        ServerUrl = coordinator.ServerUrl;
        IsPaired = coordinator.IsPaired;

        coordinator.DeviceIdChanged += OnDeviceIdChanged;
        coordinator.ServerUrlChanged += OnServerUrlChanged;
        coordinator.IsPairedChanged += OnIsPairedChanged;
    }

    #endregion

    #region Private methods

    private void OnDeviceIdChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() => DeviceId = coordinator.DeviceId);
    }

    private void OnServerUrlChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() => ServerUrl = coordinator.ServerUrl);
    }

    private void OnIsPairedChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() => IsPaired = coordinator.IsPaired);
    }

    #endregion Private methods

    #region Commands

    [RelayCommand]
    private async Task RequestPairing()
    {
        Debug.Assert(DeviceId is not null);

        var result = await coordinator.RequestPairingAsync(DeviceId);
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
        Debug.Assert(DeviceId is not null);

        var result = await coordinator.ConfirmPairingAsync(DeviceId, Pin);
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
        Debug.Assert(DeviceId is not null);

        var result = await coordinator.UnpairAsync(DeviceId);
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

    [RelayCommand]
    private void OpenPreviousPage() => shell.GoBack();

    #endregion Commands
}

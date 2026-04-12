using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;

namespace Sufni.App.ViewModels.Editors;

// One live-preview tab. It owns one client instance, the requested-rate inputs, and
// the throttled snapshot projected into the desktop detail view.
public sealed partial class LiveDaqDetailViewModel : TabPageViewModelBase
{
    public string IdentityKey { get; }
    public string? BoardId { get; }
    public string? Endpoint { get; }
    public string? SetupName { get; }
    public string? BikeName { get; }

    private readonly ILiveDaqClient? liveDaqClient;
    private readonly LiveDaqSessionState sessionState = new();
    private readonly DispatcherTimer uiRefreshTimer;
    private readonly CancellableOperation connectOperation = new();
    private readonly object stateGate = new();
    private LiveConnectionState connectionState = LiveConnectionState.Disconnected;
    private string? lastError;

    [ObservableProperty]
    private uint? requestedTravelHz;

    [ObservableProperty]
    private uint? requestedImuHz;

    [ObservableProperty]
    private uint? requestedGpsFixHz;

    [ObservableProperty]
    private LiveDaqUiSnapshot snapshot = LiveDaqUiSnapshot.Empty;

    public LiveDaqDetailViewModel()
    {
        IdentityKey = string.Empty;
        uiRefreshTimer = CreateUiRefreshTimer();
    }

    public LiveDaqDetailViewModel(
        LiveDaqSnapshot snapshot,
        ILiveDaqClientFactory liveDaqClientFactory,
        IShellCoordinator shell,
        IDialogService dialogService)
        : base(shell, dialogService)
    {
        IdentityKey = snapshot.IdentityKey;
        BoardId = snapshot.BoardId;
        Endpoint = snapshot.Endpoint;
        SetupName = snapshot.SetupName;
        BikeName = snapshot.BikeName;
        Name = snapshot.DisplayName;
        liveDaqClient = liveDaqClientFactory.CreateClient();
        uiRefreshTimer = CreateUiRefreshTimer();
        RefreshSnapshot();
    }

    [RelayCommand]
    private async Task Loaded()
    {
        uiRefreshTimer.Start();
        EnsureScopedSubscription(disposables =>
        {
            if (liveDaqClient is null)
            {
                return;
            }

            disposables.Add(liveDaqClient.Events.Subscribe(HandleClientEvent));
        });

        RefreshSnapshot();
        await ConnectImplementationAsync();
    }

    [RelayCommand]
    private void Unloaded()
    {
        connectOperation.Cancel();
        uiRefreshTimer.Stop();
        DisposeScopedSubscriptions();
        _ = DisconnectImplementationAsync();
    }

    [RelayCommand]
    private async Task Connect()
    {
        await ConnectImplementationAsync();
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        await DisconnectImplementationAsync();
    }

    private async Task ConnectImplementationAsync()
    {
        if (liveDaqClient is null || GetConnectionState() is LiveConnectionState.Connecting or LiveConnectionState.Connected)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Endpoint) || !TryGetEndpoint(out var host, out var port))
        {
            SetConnectionState(LiveConnectionState.Disconnected, "DAQ is offline.");
            return;
        }

        sessionState.Reset();
        SetConnectionState(LiveConnectionState.Connecting, null);
        var cancellationToken = connectOperation.Start();

        try
        {
            if (!liveDaqClient.IsConnected)
            {
                await liveDaqClient.ConnectAsync(host, port, cancellationToken);
            }

            var result = await liveDaqClient.StartPreviewAsync(CreateStartRequest(), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            switch (result)
            {
                case LivePreviewStartResult.Started started:
                    sessionState.ApplyFrame(new LiveSessionHeaderFrame(
                        new LiveFrameHeader(
                            LiveProtocolConstants.Magic,
                            LiveProtocolConstants.Version,
                            LiveFrameType.SessionHeader,
                            LiveProtocolConstants.SessionHeaderPayloadSize,
                            0),
                        started.Header));
                    SetConnectionState(LiveConnectionState.Connected, null);
                    break;

                case LivePreviewStartResult.Rejected rejected:
                    SetConnectionState(LiveConnectionState.Disconnected, rejected.UserMessage);
                    await liveDaqClient.DisconnectAsync(cancellationToken);
                    await dialogService.ShowConfirmationAsync("Live Preview Unavailable", rejected.UserMessage);
                    shell.Close(this);
                    break;

                case LivePreviewStartResult.Failed failed:
                    SetConnectionState(LiveConnectionState.Disconnected, failed.ErrorMessage);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            SetConnectionState(LiveConnectionState.Disconnected, ex.Message);
        }
    }

    private async Task DisconnectImplementationAsync()
    {
        if (liveDaqClient is null)
        {
            return;
        }

        SetConnectionState(LiveConnectionState.Disconnecting, null);
        try
        {
            await liveDaqClient.DisconnectAsync();
            SetConnectionState(LiveConnectionState.Disconnected, null);
        }
        catch (Exception ex)
        {
            SetConnectionState(LiveConnectionState.Disconnected, ex.Message);
        }
    }

    private void HandleClientEvent(LiveDaqClientEvent clientEvent)
    {
        switch (clientEvent)
        {
            case LiveDaqClientEvent.FrameReceived frameReceived:
                sessionState.ApplyFrame(frameReceived.Frame);
                if (frameReceived.Frame is LiveSessionHeaderFrame)
                {
                    SetConnectionState(LiveConnectionState.Connected, null);
                }
                else if (frameReceived.Frame is LiveErrorFrame errorFrame)
                {
                    SetConnectionState(LiveConnectionState.Disconnected, errorFrame.Payload.ErrorCode.ToUserMessage());
                }
                break;

            case LiveDaqClientEvent.Faulted faulted:
                SetConnectionState(LiveConnectionState.Disconnected, faulted.ErrorMessage);
                break;

            case LiveDaqClientEvent.Disconnected disconnected:
                SetConnectionState(LiveConnectionState.Disconnected, disconnected.ErrorMessage);
                break;
        }
    }

    private LiveStartRequest CreateStartRequest() => new(
        SensorMask: LiveSensorMask.Travel | LiveSensorMask.Imu,
        TravelHz: RequestedTravelHz ?? 0,
        ImuHz: RequestedImuHz ?? 0,
        GpsFixHz: RequestedGpsFixHz ?? 0);

    private void RefreshSnapshot()
    {
        Snapshot = sessionState.CreateSnapshot(GetConnectionState(), GetLastError());
    }

    private void SetConnectionState(LiveConnectionState state, string? error)
    {
        lock (stateGate)
        {
            connectionState = state;
            lastError = error;
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshSnapshot();
            return;
        }

        Dispatcher.UIThread.Post(RefreshSnapshot, DispatcherPriority.Background);
    }

    private LiveConnectionState GetConnectionState()
    {
        lock (stateGate)
        {
            return connectionState;
        }
    }

    private string? GetLastError()
    {
        lock (stateGate)
        {
            return lastError;
        }
    }

    private bool TryGetEndpoint(out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(Endpoint))
        {
            return false;
        }

        var parts = Endpoint.Split(':', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out port))
        {
            return false;
        }

        host = parts[0];
        return true;
    }

    private DispatcherTimer CreateUiRefreshTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        timer.Tick += (_, _) => RefreshSnapshot();
        return timer;
    }
}
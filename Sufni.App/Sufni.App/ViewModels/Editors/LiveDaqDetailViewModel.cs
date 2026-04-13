using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Serilog;

namespace Sufni.App.ViewModels.Editors;

// One live-preview tab. It owns one client instance, the requested-rate inputs, and
// the throttled snapshot projected into the desktop detail view.
public sealed partial class LiveDaqDetailViewModel : TabPageViewModelBase
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqDetailViewModel>();

    public string IdentityKey { get; }
    public string? BoardId { get; }
    public string? Endpoint { get; }
    public string? SetupName { get; }
    public string? BikeName { get; }

    private readonly ILiveDaqClient? liveDaqClient;
    private readonly ILiveDaqKnownBoardsQuery? knownBoardsQuery;
    private readonly LiveDaqSessionState sessionState = new();
    private readonly DispatcherTimer uiRefreshTimer;
    private readonly CancellableOperation connectOperation = new();
    private readonly object stateGate = new();
    private LiveConnectionState connectionState = LiveConnectionState.Disconnected;
    private string? lastError;
    private LiveDaqTravelCalibration? travelCalibration;

    [ObservableProperty]
    private uint? requestedTravelHz;

    [ObservableProperty]
    private uint? requestedImuHz;

    [ObservableProperty]
    private uint? requestedGpsFixHz;

    [ObservableProperty]
    private LiveDaqUiSnapshot snapshot = LiveDaqUiSnapshot.Empty;

    public string FrontTravelText => FormatTravelText("Front", Snapshot.Travel.FrontMeasurement, travelCalibration?.Front);

    public string RearTravelText => FormatTravelText("Rear", Snapshot.Travel.RearMeasurement, travelCalibration?.Rear);

    public LiveDaqDetailViewModel()
    {
        IdentityKey = string.Empty;
        uiRefreshTimer = CreateUiRefreshTimer();
    }

    public LiveDaqDetailViewModel(
        LiveDaqSnapshot snapshot,
        ILiveDaqClientFactory liveDaqClientFactory,
        IShellCoordinator shell,
        IDialogService dialogService,
        ILiveDaqKnownBoardsQuery knownBoardsQuery)
        : base(shell, dialogService)
    {
        IdentityKey = snapshot.IdentityKey;
        BoardId = snapshot.BoardId;
        Endpoint = snapshot.Endpoint;
        SetupName = snapshot.SetupName;
        BikeName = snapshot.BikeName;
        Name = snapshot.DisplayName;
        liveDaqClient = liveDaqClientFactory.CreateClient();
        this.knownBoardsQuery = knownBoardsQuery;
        RefreshTravelCalibration();
        uiRefreshTimer = CreateUiRefreshTimer();
        RefreshSnapshot();
    }

    [RelayCommand]
    private async Task Loaded()
    {
        logger.Debug(
            "Live DAQ detail loaded for {IdentityKey} {Endpoint}",
            IdentityKey,
            Endpoint);

        uiRefreshTimer.Start();
        EnsureScopedSubscription(disposables =>
        {
            if (liveDaqClient is null)
            {
                return;
            }

            disposables.Add(liveDaqClient.Events.Subscribe(HandleClientEvent));
            if (knownBoardsQuery is not null)
            {
                disposables.Add(knownBoardsQuery.Changes.Subscribe(_ => RequestTravelProjectionRefresh()));
            }
        });

        RefreshSnapshot();
        await ConnectImplementationAsync(userInitiated: false);
    }

    [RelayCommand]
    private async Task Unloaded()
    {
        logger.Debug(
            "Live DAQ detail unloaded for {IdentityKey} {Endpoint}; disconnect already in progress: {DisconnectInProgress}",
            IdentityKey,
            Endpoint,
            GetConnectionState() == LiveConnectionState.Disconnecting);

        await DeactivateAsync();
    }

    protected override async Task CloseImplementation()
    {
        await DeactivateAsync();
    }

    private async Task DeactivateAsync()
    {
        connectOperation.Cancel();
        uiRefreshTimer.Stop();
        DisposeScopedSubscriptions();
        await DisconnectImplementationAsync(userInitiated: false);
    }

    [RelayCommand]
    private async Task Connect()
    {
        await ConnectImplementationAsync(userInitiated: true);
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        await DisconnectImplementationAsync(userInitiated: true);
    }

    private async Task ConnectImplementationAsync(bool userInitiated)
    {
        if (liveDaqClient is null || GetConnectionState() is LiveConnectionState.Connecting or LiveConnectionState.Connected)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Endpoint) || !TryGetEndpoint(out var host, out var port))
        {
            if (userInitiated)
            {
                logger.Warning(
                    "Live DAQ connect requested while endpoint was unavailable for {IdentityKey} {BoardId} {Endpoint}",
                    IdentityKey,
                    BoardId,
                    Endpoint);
            }

            SetConnectionState(LiveConnectionState.Disconnected, "DAQ is offline.");
            return;
        }

        logger.Information(
            "Connecting live DAQ preview for {IdentityKey} {BoardId} {Endpoint}",
            IdentityKey,
            BoardId,
            Endpoint);
        logger.Verbose(
            "Live DAQ preview requested rates for {IdentityKey} at {Endpoint}: travel {RequestedTravelHz}, imu {RequestedImuHz}, gps {RequestedGpsFixHz}",
            IdentityKey,
            Endpoint,
            RequestedTravelHz,
            RequestedImuHz,
            RequestedGpsFixHz);

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
                    logger.Verbose(
                        "Live DAQ preview session accepted for {IdentityKey} at {Endpoint}: session {SessionId}, sensors {SelectedSensorMask}, travel {AcceptedTravelHz}, imu {AcceptedImuHz}, gps {AcceptedGpsFixHz}, active imu {ActiveImuMask}",
                        IdentityKey,
                        Endpoint,
                        started.Header.SessionId,
                        started.SelectedSensorMask,
                        started.Header.AcceptedTravelHz,
                        started.Header.AcceptedImuHz,
                        started.Header.AcceptedGpsFixHz,
                        started.Header.ActiveImuMask);
                    logger.Information(
                        "Live DAQ preview connected for {IdentityKey} {BoardId} {Endpoint}",
                        IdentityKey,
                        BoardId,
                        Endpoint);
                    break;

                case LivePreviewStartResult.Rejected rejected:
                    LogRejectedStart(rejected);
                    SetConnectionState(LiveConnectionState.Disconnected, rejected.UserMessage);
                    await liveDaqClient.DisconnectAsync(cancellationToken);
                    await dialogService.ShowConfirmationAsync("Live Preview Unavailable", rejected.UserMessage);
                    shell.Close(this);
                    break;

                case LivePreviewStartResult.Failed failed:
                    logger.Error(
                        "Live DAQ preview failed to start for {IdentityKey} {BoardId} {Endpoint}: {ErrorMessage}",
                        IdentityKey,
                        BoardId,
                        Endpoint,
                        failed.ErrorMessage);
                    SetConnectionState(LiveConnectionState.Disconnected, failed.ErrorMessage);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.Error(
                ex,
                "Live DAQ preview connection failed for {IdentityKey} {BoardId} {Endpoint}",
                IdentityKey,
                BoardId,
                Endpoint);
            SetConnectionState(LiveConnectionState.Disconnected, ex.Message);
        }
    }

    private async Task DisconnectImplementationAsync(bool userInitiated)
    {
        if (liveDaqClient is null)
        {
            return;
        }

        var currentState = GetConnectionState();
        if (!liveDaqClient.IsConnected && currentState is LiveConnectionState.Disconnected)
        {
            return;
        }

        logger.Information(
            "Disconnecting live DAQ preview for {IdentityKey} {BoardId} {Endpoint}",
            IdentityKey,
            BoardId,
            Endpoint);

        SetConnectionState(LiveConnectionState.Disconnecting, null);
        try
        {
            await liveDaqClient.DisconnectAsync();
            logger.Information(
                "Live DAQ preview disconnected for {IdentityKey} {BoardId} {Endpoint}",
                IdentityKey,
                BoardId,
                Endpoint);
            SetConnectionState(LiveConnectionState.Disconnected, null);
        }
        catch (Exception ex)
        {
            logger.Error(
                ex,
                "Live DAQ preview disconnect failed for {IdentityKey} {BoardId} {Endpoint}",
                IdentityKey,
                BoardId,
                Endpoint);
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
                    logger.Debug(
                        "Live DAQ client reported error {ErrorCode} for {IdentityKey} at {Endpoint}: {ErrorMessage}",
                        errorFrame.Payload.ErrorCode,
                        IdentityKey,
                        Endpoint,
                        errorFrame.Payload.ErrorCode.ToUserMessage());
                    SetConnectionState(LiveConnectionState.Disconnected, errorFrame.Payload.ErrorCode.ToUserMessage());
                }
                break;

            case LiveDaqClientEvent.Faulted faulted:
                logger.Verbose(
                    "Live DAQ client faulted for {IdentityKey} at {Endpoint}: {ErrorMessage}",
                    IdentityKey,
                    Endpoint,
                    faulted.ErrorMessage);
                SetConnectionState(LiveConnectionState.Disconnected, faulted.ErrorMessage);
                break;

            case LiveDaqClientEvent.Disconnected disconnected:
                if (!string.IsNullOrWhiteSpace(disconnected.ErrorMessage))
                {
                    logger.Verbose(
                        "Live DAQ client disconnected for {IdentityKey} at {Endpoint}: {ErrorMessage}",
                        IdentityKey,
                        Endpoint,
                        disconnected.ErrorMessage);
                }

                SetConnectionState(LiveConnectionState.Disconnected, disconnected.ErrorMessage);
                break;
        }
    }

    private void LogRejectedStart(LivePreviewStartResult.Rejected rejected)
    {
        if (rejected.ErrorCode is LiveStartErrorCode.Busy or LiveStartErrorCode.Unavailable)
        {
            logger.Warning(
                "Live DAQ preview rejected for {IdentityKey} {BoardId} {Endpoint} with {ErrorCode}: {ErrorMessage}",
                IdentityKey,
                BoardId,
                Endpoint,
                rejected.ErrorCode,
                rejected.UserMessage);
            return;
        }

        logger.Error(
            "Live DAQ preview rejected for {IdentityKey} {BoardId} {Endpoint} with {ErrorCode}: {ErrorMessage}",
            IdentityKey,
            BoardId,
            Endpoint,
            rejected.ErrorCode,
            rejected.UserMessage);
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

    partial void OnSnapshotChanged(LiveDaqUiSnapshot value)
    {
        OnPropertyChanged(nameof(FrontTravelText));
        OnPropertyChanged(nameof(RearTravelText));
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

    private void RequestTravelProjectionRefresh()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshTravelCalibration();
            return;
        }

        Dispatcher.UIThread.Post(RefreshTravelCalibration, DispatcherPriority.Background);
    }

    private void RefreshTravelCalibration()
    {
        travelCalibration = knownBoardsQuery?.GetTravelCalibration(IdentityKey);
        OnPropertyChanged(nameof(FrontTravelText));
        OnPropertyChanged(nameof(RearTravelText));
    }

    private static string FormatTravelText(string label, ushort? measurement, LiveDaqTravelChannelCalibration? calibration)
    {
        if (measurement is not ushort rawMeasurement)
        {
            return $"{label}: -";
        }

        if (calibration is null)
        {
            return $"{label}: {rawMeasurement}";
        }

        var travel = calibration.MeasurementToTravel(rawMeasurement);
        if (double.IsNaN(travel) || double.IsInfinity(travel))
        {
            return $"{label}: {rawMeasurement}";
        }

        travel = Math.Clamp(travel, 0, calibration.MaxTravel);
        var sagPercent = travel / calibration.MaxTravel * 100.0;
        return FormattableString.Invariant($"{label}: {travel:0}mm ({sagPercent:0}%)");
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

    // Frames arrive far faster than the UI needs to repaint. The session state
    // accumulates every frame, and this timer snapshots it at a fixed cadence
    // so the UI thread is not overwhelmed by per-frame updates.
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
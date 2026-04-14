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

    private readonly ILiveDaqSharedStream? sharedStream;
    private readonly ILiveDaqCoordinator? liveDaqCoordinator;
    private readonly ILiveDaqKnownBoardsQuery? knownBoardsQuery;
    private readonly LiveDaqSessionState sessionState = new();
    private readonly DispatcherTimer uiRefreshTimer;
    private readonly CancellableOperation connectOperation = new();
    private ILiveDaqSharedStreamLease? streamLease;
    private LiveDaqTravelCalibration? travelCalibration;

    [ObservableProperty]
    private uint? requestedTravelHz;

    [ObservableProperty]
    private uint? requestedImuHz;

    [ObservableProperty]
    private uint? requestedGpsFixHz;

    [ObservableProperty]
    private LiveDaqUiSnapshot snapshot = LiveDaqUiSnapshot.Empty;

    [ObservableProperty]
    private bool canConnect = true;

    [ObservableProperty]
    private bool canDisconnect;

    [ObservableProperty]
    private bool areRequestedRatesEnabled = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSessionCommand))]
    private bool canStartSession;

    public string FrontTravelText => FormatTravelText("Front", Snapshot.Travel.FrontMeasurement, travelCalibration?.Front);

    public string RearTravelText => FormatTravelText("Rear", Snapshot.Travel.RearMeasurement, travelCalibration?.Rear);

    public LiveDaqDetailViewModel()
    {
        IdentityKey = string.Empty;
        uiRefreshTimer = CreateUiRefreshTimer();
    }

    public LiveDaqDetailViewModel(
        LiveDaqSnapshot snapshot,
        ILiveDaqSharedStream sharedStream,
        ILiveDaqCoordinator liveDaqCoordinator,
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
        this.sharedStream = sharedStream;
        this.liveDaqCoordinator = liveDaqCoordinator;
        this.knownBoardsQuery = knownBoardsQuery;
        ApplyRequestedRates(sharedStream.RequestedConfiguration);
        RefreshTravelCalibration();
        RefreshSessionAvailability();
        uiRefreshTimer = CreateUiRefreshTimer();
        RefreshSharedStreamState();
        RefreshSnapshot();
    }

    [RelayCommand]
    private async Task Loaded()
    {
        logger.Debug(
            "Live DAQ detail loaded for {IdentityKey} {Endpoint}",
            IdentityKey,
            Endpoint);

        if (sharedStream is not null && streamLease is null)
        {
            streamLease = sharedStream.AcquireLease();
        }

        uiRefreshTimer.Start();
        EnsureScopedSubscription(disposables =>
        {
            if (sharedStream is null)
            {
                return;
            }

            disposables.Add(sharedStream.Frames.Subscribe(frame => HandleFrame(frame)));
            disposables.Add(sharedStream.States.Subscribe(_ => RequestSharedStreamRefresh()));
            if (knownBoardsQuery is not null)
            {
                disposables.Add(knownBoardsQuery.Changes.Subscribe(_ =>
                {
                    RequestTravelProjectionRefresh();
                    RequestSessionAvailabilityRefresh();
                }));
            }
        });

        RefreshSharedStreamState();
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
            sharedStream?.CurrentState.ConnectionState == LiveConnectionState.Disconnecting);

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

        if (streamLease is not null)
        {
            await streamLease.DisposeAsync();
            streamLease = null;
        }
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

    [RelayCommand(CanExecute = nameof(CanStartSession))]
    private async Task StartSession()
    {
        if (liveDaqCoordinator is null)
        {
            return;
        }

        await liveDaqCoordinator.OpenSessionAsync(IdentityKey);
    }

    private async Task ConnectImplementationAsync(bool userInitiated)
    {
        if (sharedStream is null)
        {
            return;
        }

        var streamState = sharedStream.CurrentState;
        if (streamState.IsClosed || streamState.ConnectionState is LiveConnectionState.Connecting or LiveConnectionState.Connected)
        {
            return;
        }

        logger.Information(
            "Connecting live DAQ preview for {IdentityKey} {BoardId} {Endpoint}",
            IdentityKey,
            BoardId,
            Endpoint);

        sessionState.Reset();
        var cancellationToken = connectOperation.Start();

        try
        {
            await sharedStream.ApplyConfigurationAsync(CreateRequestedConfiguration(), cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var result = await sharedStream.EnsureStartedAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var updatedState = sharedStream.CurrentState;
            if (result is LivePreviewStartResult.Rejected rejected)
            {
                await dialogService.ShowConfirmationAsync("Live Preview Unavailable", rejected.UserMessage);
                shell.Close(this);
            }
            else if (updatedState.ConnectionState == LiveConnectionState.Connected)
            {
                logger.Information(
                    "Live DAQ preview connected for {IdentityKey} {BoardId} {Endpoint}",
                    IdentityKey,
                    BoardId,
                    Endpoint);
            }
            else if (userInitiated && !string.IsNullOrWhiteSpace(updatedState.LastError))
            {
                logger.Warning(
                    "Live DAQ preview connect failed for {IdentityKey} {BoardId} {Endpoint}: {ErrorMessage}",
                    IdentityKey,
                    BoardId,
                    Endpoint,
                    updatedState.LastError);
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
        }

        RefreshSharedStreamState();
    }

    private async Task DisconnectImplementationAsync(bool userInitiated)
    {
        if (sharedStream is null)
        {
            return;
        }

        var currentState = sharedStream.CurrentState;
        if (currentState.IsClosed || currentState.ConnectionState is LiveConnectionState.Disconnected)
        {
            return;
        }

        logger.Information(
            "Disconnecting live DAQ preview for {IdentityKey} {BoardId} {Endpoint}",
            IdentityKey,
            BoardId,
            Endpoint);

        try
        {
            await sharedStream.StopAsync();
            logger.Information(
                "Live DAQ preview disconnected for {IdentityKey} {BoardId} {Endpoint}",
                IdentityKey,
                BoardId,
                Endpoint);
        }
        catch (Exception ex)
        {
            logger.Error(
                ex,
                "Live DAQ preview disconnect failed for {IdentityKey} {BoardId} {Endpoint}",
                IdentityKey,
                BoardId,
                Endpoint);
        }

        RefreshSharedStreamState();
    }

    private void HandleFrame(LiveProtocolFrame frame)
    {
        sessionState.ApplyFrame(frame);
    }

    private LiveDaqStreamConfiguration CreateRequestedConfiguration() => new(
        SensorMask: LiveSensorMask.Travel | LiveSensorMask.Imu,
        TravelHz: RequestedTravelHz ?? 0,
        ImuHz: RequestedImuHz ?? 0,
        GpsFixHz: RequestedGpsFixHz ?? 0);

    private void RefreshSnapshot()
    {
        var state = sharedStream?.CurrentState ?? LiveDaqSharedStreamState.Empty;
        sessionState.ApplySharedSessionState(state.SessionHeader, state.SelectedSensorMask);
        Snapshot = sessionState.CreateSnapshot(state.ConnectionState, state.LastError);
    }

    partial void OnSnapshotChanged(LiveDaqUiSnapshot value)
    {
        OnPropertyChanged(nameof(FrontTravelText));
        OnPropertyChanged(nameof(RearTravelText));
    }

    private void RequestSharedStreamRefresh()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshSharedStreamState();
            return;
        }

        Dispatcher.UIThread.Post(RefreshSharedStreamState, DispatcherPriority.Background);
    }

    private void RefreshSharedStreamState()
    {
        var state = sharedStream?.CurrentState ?? LiveDaqSharedStreamState.Empty;
        sessionState.ApplySharedSessionState(state.SessionHeader, state.SelectedSensorMask);
        CanConnect = !state.IsClosed && state.ConnectionState == LiveConnectionState.Disconnected;
        CanDisconnect = !state.IsClosed && state.ConnectionState == LiveConnectionState.Connected;
        AreRequestedRatesEnabled = !state.IsClosed && !state.IsConfigurationLocked;
        RefreshSnapshot();
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

    private void RequestSessionAvailabilityRefresh()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshSessionAvailability();
            return;
        }

        Dispatcher.UIThread.Post(RefreshSessionAvailability, DispatcherPriority.Background);
    }

    private void RefreshTravelCalibration()
    {
        travelCalibration = knownBoardsQuery?.GetTravelCalibration(IdentityKey);
        OnPropertyChanged(nameof(FrontTravelText));
        OnPropertyChanged(nameof(RearTravelText));
    }

    private void RefreshSessionAvailability()
    {
        CanStartSession = knownBoardsQuery?.GetSessionContext(IdentityKey) is not null;
    }

    private void ApplyRequestedRates(LiveDaqStreamConfiguration configuration)
    {
        RequestedTravelHz = configuration.TravelHz;
        RequestedImuHz = configuration.ImuHz;
        RequestedGpsFixHz = configuration.GpsFixHz;
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
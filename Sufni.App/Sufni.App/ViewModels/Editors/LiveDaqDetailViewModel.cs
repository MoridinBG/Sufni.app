using System;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.Management;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Serilog;

namespace Sufni.App.ViewModels.Editors;

// One live-preview tab. It owns the requested-rate inputs plus a shared-stream observer
// lease, and projects a throttled snapshot into the desktop detail view.
public sealed partial class LiveDaqDetailViewModel : TabPageViewModelBase
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqDetailViewModel>();

    public string IdentityKey { get; }

    [ObservableProperty]
    private string? boardId;

    [ObservableProperty]
    private string? endpoint;

    [ObservableProperty]
    private string? setupName;

    [ObservableProperty]
    private string? bikeName;

    private readonly ILiveDaqSharedStream sharedStream;
    private readonly LiveDaqCoordinator liveDaqCoordinator;
    private readonly IDaqManagementService daqManagementService;
    private readonly IFilesService filesService;
    private readonly ILiveDaqKnownBoardsQuery knownBoardsQuery;
    private readonly ILiveDaqStore liveDaqStore;

    private readonly LiveDaqSessionState sessionState = new();
    private readonly DispatcherTimer uiRefreshTimer;
    private readonly CancellableOperation connectOperation = new();
    private readonly CancellableOperation managementOperation = new();
    private string? managementHost;
    private int? managementPort;
    private ILiveDaqSharedStreamLease? streamLease;
    private LiveDaqTravelCalibration? travelCalibration;
    private byte[]? pendingConfigBytes;
    private bool hasLoaded;

    [ObservableProperty]
    private uint? requestedTravelHz;

    [ObservableProperty]
    private uint? requestedImuHz;

    [ObservableProperty]
    private uint? requestedGpsFixHz;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrontTravelText))]
    [NotifyPropertyChangedFor(nameof(RearTravelText))]
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

    [ObservableProperty]
    private bool isManagementBusy;

    [ObservableProperty]
    private bool hasPendingConfig;

    [ObservableProperty]
    private string? pendingConfigFileName;

    public string FrontTravelText => FormatTravelText("Front", Snapshot.HasAcceptedSession, Snapshot.Travel.FrontIsActive, Snapshot.Travel.FrontMeasurement, travelCalibration?.Front);

    public string RearTravelText => FormatTravelText("Rear", Snapshot.HasAcceptedSession, Snapshot.Travel.RearIsActive, Snapshot.Travel.RearMeasurement, travelCalibration?.Rear);

    public bool CanManage => Snapshot.ConnectionState is LiveConnectionState.Disconnected
        && !string.IsNullOrWhiteSpace(managementHost)
        && managementPort is > 0
        && !IsManagementBusy;

    public bool CanUploadConfig => CanManage && HasPendingConfig;

    public string? ManagementDisabledTooltip => !string.IsNullOrWhiteSpace(managementHost)
        && managementPort is > 0
        && Snapshot.ConnectionState is not LiveConnectionState.Disconnected
            ? "Disconnect live session first"
            : null;

    public string? ConnectDisabledTooltip => sharedStream.CurrentState.ConnectionState == LiveConnectionState.Disconnected
        && !HasRequestedSensors
            ? "Set at least one live preview rate above 0 Hz."
            : null;

    public LiveDaqDetailViewModel(
        LiveDaqSnapshot snapshot,
        ILiveDaqSharedStream sharedStream,
        LiveDaqCoordinator liveDaqCoordinator,
        IDaqManagementService daqManagementService,
        IFilesService filesService,
        IShellCoordinator shell,
        IDialogService dialogService,
        ILiveDaqKnownBoardsQuery knownBoardsQuery,
        ILiveDaqStore liveDaqStore)
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
        this.daqManagementService = daqManagementService;
        this.filesService = filesService;
        this.knownBoardsQuery = knownBoardsQuery;
        this.liveDaqStore = liveDaqStore;
        managementHost = snapshot.Host;
        managementPort = snapshot.Port;
        ApplyRequestedRates(sharedStream.RequestedConfiguration);
        RefreshTravelCalibration();
        uiRefreshTimer = CreateUiRefreshTimer();
        RefreshSharedStreamState();
        RefreshSnapshot();
    }

    [RelayCommand]
    private Task Loaded()
    {
        logger.Debug(
            "Live DAQ detail loaded for {IdentityKey} {Endpoint}",
            IdentityKey,
            Endpoint);

        if (streamLease is null)
        {
            streamLease = sharedStream.AcquireLease();
        }

        hasLoaded = true;
        StartForegroundUpdates();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task Unloaded()
    {
        logger.Debug(
            "Live DAQ detail unloaded for {IdentityKey} {Endpoint}; disconnect already in progress: {DisconnectInProgress}",
            IdentityKey,
            Endpoint,
            sharedStream.CurrentState.ConnectionState == LiveConnectionState.Disconnecting);

        await DeactivateAsync();
    }

    protected override async Task CloseImplementation()
    {
        await DeactivateAsync();
    }

    protected override bool CanSave() => false;
    protected override bool CanReset() => false;
    protected override bool CanExport() => false;

    private async Task DeactivateAsync()
    {
        connectOperation.Cancel();
        SetTimeCommand.Cancel();
        SelectConfigFileCommand.Cancel();
        UploadConfigCommand.Cancel();
        EditConfigCommand.Cancel();
        managementOperation.Cancel();
        IsManagementBusy = false;
        hasLoaded = false;
        StopForegroundUpdates();

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
        await liveDaqCoordinator.OpenSessionAsync(IdentityKey);
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task SetTime(CancellationToken cancellationToken)
    {
        if (!TryGetManagementEndpoint(out var host, out var port))
        {
            return;
        }

        using var linkedCts = BeginManagementOperation(cancellationToken);
        try
        {
            var result = await daqManagementService.SetTimeAsync(host, port, linkedCts.Token);
            if (linkedCts.IsCancellationRequested)
            {
                return;
            }

            switch (result)
            {
                case DaqSetTimeResult.Ok ok:
                    Notifications.Add(FormattableString.Invariant($"Device time updated ({ok.RoundTripTime.TotalMilliseconds:0} ms RTT)."));
                    break;
                case DaqSetTimeResult.Error error:
                    ErrorMessages.Add(error.Message);
                    break;
            }
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!linkedCts.IsCancellationRequested)
            {
                logger.Error(ex, "Setting device time failed for {IdentityKey} {Endpoint}", IdentityKey, Endpoint);
                ErrorMessages.Add(ex.Message);
            }
        }
        finally
        {
            EndManagementOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task SelectConfigFile(CancellationToken cancellationToken)
    {
        using var linkedCts = BeginManagementOperation(cancellationToken);
        try
        {
            var selectedFile = await filesService.OpenDeviceConfigFileAsync(linkedCts.Token);
            if (linkedCts.IsCancellationRequested || selectedFile is null)
            {
                return;
            }

            if (!string.Equals(selectedFile.FileName, "CONFIG", StringComparison.Ordinal))
            {
                ErrorMessages.Add("Selected file must be named CONFIG.");
                return;
            }

            pendingConfigBytes = selectedFile.Bytes;
            PendingConfigFileName = selectedFile.FileName;
            HasPendingConfig = true;
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!linkedCts.IsCancellationRequested)
            {
                logger.Error(ex, "Selecting CONFIG failed for {IdentityKey} {Endpoint}", IdentityKey, Endpoint);
                ErrorMessages.Add(ex.Message);
            }
        }
        finally
        {
            EndManagementOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanUploadConfig))]
    private async Task UploadConfig(CancellationToken cancellationToken)
    {
        if (pendingConfigBytes is not { Length: > 0 } configBytes
            || !TryGetManagementEndpoint(out var host, out var port))
        {
            return;
        }

        using var linkedCts = BeginManagementOperation(cancellationToken);
        var uploadStarted = false;
        try
        {
            uploadStarted = true;
            var result = await daqManagementService.ReplaceConfigAsync(host, port, configBytes, linkedCts.Token);
            if (linkedCts.IsCancellationRequested)
            {
                return;
            }

            switch (result)
            {
                case DaqManagementResult.Ok:
                    Notifications.Add("CONFIG uploaded.");
                    break;
                case DaqManagementResult.Error error:
                    ErrorMessages.Add(error.Message);
                    break;
            }
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (!linkedCts.IsCancellationRequested)
            {
                logger.Error(ex, "Uploading CONFIG failed for {IdentityKey} {Endpoint}", IdentityKey, Endpoint);
                ErrorMessages.Add(ex.Message);
            }
        }
        finally
        {
            if (uploadStarted)
            {
                ClearPendingConfig();
            }

            EndManagementOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task EditConfig(CancellationToken cancellationToken)
    {
        if (!TryGetManagementEndpoint(out var host, out var port))
        {
            return;
        }

        DaqConfigDocument? document = null;
        using (var linkedCts = BeginManagementOperation(cancellationToken))
        {
            try
            {
                using var destination = new MemoryStream();
                var result = await daqManagementService.GetFileAsync(
                    host,
                    port,
                    DaqFileClass.Config,
                    0,
                    destination,
                    linkedCts.Token);
                if (linkedCts.IsCancellationRequested)
                {
                    return;
                }

                switch (result)
                {
                    case DaqGetFileResult.Downloaded:
                        document = DaqConfigDocument.Parse(destination.ToArray());
                        break;
                    case DaqGetFileResult.Error error:
                        ErrorMessages.Add(error.Message);
                        return;
                }
            }
            catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (!linkedCts.IsCancellationRequested)
                {
                    logger.Error(ex, "Editing CONFIG failed for {IdentityKey} {Endpoint}", IdentityKey, Endpoint);
                    ErrorMessages.Add(ex.Message);
                }

                return;
            }
            finally
            {
                EndManagementOperation();
            }
        }

        if (document is null)
        {
            return;
        }

        var editor = new LiveDaqConfigEditorViewModel(
            document,
            (bytes, uploadToken) => UploadEditedConfigAsync(host, port, bytes, uploadToken));
        await dialogService.ShowLiveDaqConfigEditorDialogAsync(editor);
    }

    private async Task<DaqManagementResult> UploadEditedConfigAsync(
        string host,
        int port,
        byte[] configBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await daqManagementService.ReplaceConfigAsync(host, port, configBytes, cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return result;
            }

            if (result is DaqManagementResult.Ok)
            {
                Notifications.Add("CONFIG uploaded.");
            }

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Uploading edited CONFIG failed for {IdentityKey} {Endpoint}", IdentityKey, Endpoint);
            return new DaqManagementResult.Error(DaqManagementErrorCode.InternalError, ex.Message);
        }
    }

    private async Task ConnectImplementationAsync(bool userInitiated)
    {
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
                ErrorMessages.Add(rejected.UserMessage);
                return;
            }
            else if (result is LivePreviewStartResult.Started started && updatedState.ConnectionState == LiveConnectionState.Connected)
            {
                AddMissingSensorNotification(started.Header.MissingSensorMask);
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

    private LiveDaqStreamConfiguration CreateRequestedConfiguration() => LiveDaqStreamConfiguration.FromRequestedRates(
        RequestedTravelHz ?? 0,
        RequestedImuHz ?? 0,
        RequestedGpsFixHz ?? 0);

    private void AddMissingSensorNotification(LiveSensorInstanceMask missingSensorMask)
    {
        var missingSensors = LiveProtocolHelpers.GetSensorInstanceDisplayNames(missingSensorMask);
        if (missingSensors.Count == 0)
        {
            return;
        }

        Notifications.Add($"Some requested sensors did not start: {string.Join(", ", missingSensors)}.");
    }

    private void RefreshSnapshot()
    {
        var state = sharedStream.CurrentState;
        sessionState.ApplySharedSessionState(state.SessionHeader, state.SelectedSensorMask);
        Snapshot = sessionState.CreateSnapshot(state.ConnectionState, state.LastError);
    }

    partial void OnSnapshotChanged(LiveDaqUiSnapshot value)
    {
        RefreshManagementAvailability();
    }

    partial void OnIsManagementBusyChanged(bool value) => RefreshManagementAvailability();

    partial void OnHasPendingConfigChanged(bool value) => RefreshManagementAvailability();

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
        var state = sharedStream.CurrentState;
        sessionState.ApplySharedSessionState(state.SessionHeader, state.SelectedSensorMask);
        CanConnect = !state.IsClosed
            && state.ConnectionState == LiveConnectionState.Disconnected
            && HasRequestedSensors;
        CanDisconnect = !state.IsClosed && state.ConnectionState == LiveConnectionState.Connected;
        AreRequestedRatesEnabled = !state.IsClosed && !state.IsConfigurationLocked;
        RefreshSessionAvailability(state.ConnectionState);
        OnPropertyChanged(nameof(ConnectDisabledTooltip));
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
            RefreshSessionAvailability(sharedStream.CurrentState.ConnectionState);
            return;
        }

        Dispatcher.UIThread.Post(
            () => RefreshSessionAvailability(sharedStream.CurrentState.ConnectionState),
            DispatcherPriority.Background);
    }

    private void RefreshTravelCalibration()
    {
        travelCalibration = knownBoardsQuery.GetTravelCalibration(IdentityKey);
        OnPropertyChanged(nameof(FrontTravelText));
        OnPropertyChanged(nameof(RearTravelText));
    }

    private void RequestHeaderRefresh()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshHeaderFromStore();
            return;
        }

        Dispatcher.UIThread.Post(RefreshHeaderFromStore, DispatcherPriority.Background);
    }

    private void RefreshHeaderFromStore()
    {
        var latest = liveDaqStore.Get(IdentityKey);
        if (latest is not null)
        {
            Name = latest.DisplayName;
            BoardId = latest.BoardId;
            SetupName = latest.SetupName;
            BikeName = latest.BikeName;
        }

        if (TryGetCurrentEndpointSnapshot(latest, out var endpointSnapshot))
        {
            Endpoint = endpointSnapshot.Endpoint;
            managementHost = endpointSnapshot.Host;
            managementPort = endpointSnapshot.Port;
        }
        else
        {
            managementHost = null;
            managementPort = null;
        }

        RefreshManagementAvailability();
    }

    private bool TryGetCurrentEndpointSnapshot(LiveDaqSnapshot? latest, out LiveDaqSnapshot endpointSnapshot)
    {
        if (latest is { Host: not null, Port: not null })
        {
            endpointSnapshot = latest;
            return true;
        }

        if (!sharedStream.CurrentState.IsClosed
            && sharedStream.CatalogSnapshot is { Host: not null, Port: not null } sharedSnapshot)
        {
            endpointSnapshot = sharedSnapshot;
            return true;
        }

        endpointSnapshot = null!;
        return false;
    }

    private void RefreshSessionAvailability(LiveConnectionState connectionState)
    {
        CanStartSession = connectionState == LiveConnectionState.Connected
            && knownBoardsQuery.GetSessionContext(IdentityKey) is not null;
    }

    private CancellationTokenSource BeginManagementOperation(CancellationToken cancellationToken)
    {
        IsManagementBusy = true;
        var operationToken = managementOperation.Start();
        return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, operationToken);
    }

    private void EndManagementOperation()
    {
        managementOperation.Cancel();
        IsManagementBusy = false;
    }

    private void ClearPendingConfig()
    {
        pendingConfigBytes = null;
        PendingConfigFileName = null;
        HasPendingConfig = false;
    }

    private void RefreshManagementAvailability()
    {
        OnPropertyChanged(nameof(CanManage));
        OnPropertyChanged(nameof(CanUploadConfig));
        OnPropertyChanged(nameof(ManagementDisabledTooltip));
        SetTimeCommand.NotifyCanExecuteChanged();
        SelectConfigFileCommand.NotifyCanExecuteChanged();
        UploadConfigCommand.NotifyCanExecuteChanged();
        EditConfigCommand.NotifyCanExecuteChanged();
    }

    private bool TryGetManagementEndpoint(out string host, out int port)
    {
        host = managementHost ?? string.Empty;
        port = managementPort ?? 0;
        return !string.IsNullOrWhiteSpace(host) && port > 0;
    }

    protected override void OnActivated()
    {
        if (!hasLoaded)
        {
            return;
        }

        StartForegroundUpdates();
    }

    protected override void OnDeactivated()
    {
        if (!hasLoaded)
        {
            return;
        }

        StopForegroundUpdates();
    }

    private void ApplyRequestedRates(LiveDaqStreamConfiguration configuration)
    {
        RequestedTravelHz = configuration.TravelHz;
        RequestedImuHz = configuration.ImuHz;
        RequestedGpsFixHz = configuration.GpsFixHz;
    }

    partial void OnRequestedTravelHzChanged(uint? value) => RefreshConnectAvailability();

    partial void OnRequestedImuHzChanged(uint? value) => RefreshConnectAvailability();

    partial void OnRequestedGpsFixHzChanged(uint? value) => RefreshConnectAvailability();

    private bool HasRequestedSensors => CreateRequestedConfiguration().RequestedSensorMask != LiveSensorInstanceMask.None;

    private void RefreshConnectAvailability()
    {
        var state = sharedStream.CurrentState;
        CanConnect = !state.IsClosed
            && state.ConnectionState == LiveConnectionState.Disconnected
            && HasRequestedSensors;
        OnPropertyChanged(nameof(ConnectDisabledTooltip));
    }

    private static string FormatTravelText(string label, bool hasAcceptedSession, bool isActive, ushort? measurement, LiveDaqTravelChannelCalibration? calibration)
    {
        if (!hasAcceptedSession)
        {
            return $"{label}: -";
        }

        if (!isActive)
        {
            return $"{label}: inactive";
        }

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

    private void StartForegroundUpdates()
    {
        uiRefreshTimer.Start();
        EnsureScopedSubscription(disposables =>
        {
            disposables.Add(sharedStream.Frames.Subscribe(frame => HandleFrame(frame)));
            disposables.Add(sharedStream.States.Subscribe(_ => RequestSharedStreamRefresh()));
            disposables.Add(knownBoardsQuery.Changes.Subscribe(_ =>
            {
                RequestTravelProjectionRefresh();
                RequestSessionAvailabilityRefresh();
            }));
            disposables.Add(liveDaqStore.Connect().Subscribe(_ => RequestHeaderRefresh()));
        });

        RefreshSharedStreamState();
        RefreshSnapshot();
    }

    private void StopForegroundUpdates()
    {
        uiRefreshTimer.Stop();
        DisposeScopedSubscriptions();
    }
}
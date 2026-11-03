using Sufni.App.LiveDaq.Services;
using Sufni.App.Shared.Models;
using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.Shared.Base;
using Sufni.App.Shared.Common;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.LiveDaq.ViewModels.Editors;

// One live-preview tab. It owns the requested-rate inputs plus a shared-stream observer
// lease, and projects a throttled snapshot into the desktop detail view.
public sealed partial class LiveDaqDetailViewModel : TabPageViewModelBase
{
    private static readonly ILogger logger = Log.ForContext<LiveDaqDetailViewModel>();

    public string IdentityKey { get; }
    public bool IsStreamClosed => sharedStream.CurrentState.IsClosed;

    [ObservableProperty]
    public partial string? BoardId { get; set; }

    [ObservableProperty]
    public partial string? Endpoint { get; set; }

    [ObservableProperty]
    public partial string? SetupName { get; set; }

    [ObservableProperty]
    public partial string? BikeName { get; set; }

    private readonly IUiTimerScheduler uiTimerScheduler;
    private readonly ILiveDaqSharedStream sharedStream;
    private readonly ILiveDaqCoordinator coordinator;
    private readonly IDisposable managementAvailabilitySubscription;
    private LiveDaqManagementAvailability managementAvailability = new(null, null);
    private readonly ILiveDaqKnownBoardsQuery knownBoardsQuery;
    private readonly ILiveDaqStore liveDaqStore;

    private readonly LiveDaqSessionState sessionState;
    private IDisposable? uiRefreshTimer;
    private readonly CancellableOperation connectOperation = new();
    private readonly CancellableOperation managementOperation = new();
    private ILiveDaqSharedStreamLease? streamLease;
    private LiveDaqTravelCalibration? travelCalibration;
    private SelectedDeviceConfigFile? pendingConfig;
    private bool hasLoaded;

    [ObservableProperty]
    public partial uint? RequestedTravelHz { get; set; }

    [ObservableProperty]
    public partial uint? RequestedImuHz { get; set; }

    [ObservableProperty]
    public partial uint? RequestedGpsFixHz { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrontTravelText))]
    [NotifyPropertyChangedFor(nameof(RearTravelText))]
    public partial LiveDaqUiSnapshot Snapshot { get; set; } = LiveDaqUiSnapshot.Empty;

    public bool CanConnect => sharedStream.CurrentState.ConnectionState is LiveConnectionState.Disconnected
        && HasRequestedSensors && managementAvailability.CanConnect;

    public bool CanDisconnect
    {
        get
        {
            var state = sharedStream.CurrentState;
            return state.ConnectionState is LiveConnectionState.Connected && !state.IsConfigurationLocked;
        }
    }

    public bool AreRequestedRatesEnabled
    {
        get
        {
            var state = sharedStream.CurrentState;
            return !state.IsClosed && !state.IsConfigurationLocked;
        }
    }

    public bool CanStartSession => sharedStream.CurrentState.ConnectionState is LiveConnectionState.Connected
        && knownBoardsQuery.GetSessionContext(IdentityKey) is not null;

    [ObservableProperty]
    public partial bool IsManagementBusy { get; set; }

    public bool HasPendingConfig => pendingConfig is not null;
    public string? PendingConfigFileName => pendingConfig?.FileName;

    public string FrontTravelText => FormatTravelText("Front", Snapshot.HasAcceptedSession, Snapshot.Travel.FrontIsActive, Snapshot.Travel.FrontMeasurement, travelCalibration?.Front);

    public string RearTravelText => FormatTravelText("Rear", Snapshot.HasAcceptedSession, Snapshot.Travel.RearIsActive, Snapshot.Travel.RearMeasurement, travelCalibration?.Rear);

    public bool CanManage => managementAvailability.CanManage && !IsManagementBusy;
    public bool CanUploadConfig => CanManage && HasPendingConfig;
    public string? ManagementDisabledTooltip => managementAvailability.Holder is { } holder
        ? LiveDaqMessages.Busy(holder)
        : managementAvailability.Device is null ? "DAQ is offline."
        : !managementAvailability.Device.SupportsManagement ? "Management is not supported by this DAQ." : null;

    public string? ConnectDisabledTooltip => managementAvailability.Holder is { } holder ? LiveDaqMessages.Busy(holder)
        : sharedStream.CurrentState.ConnectionState is LiveConnectionState.Disconnected
        && !HasRequestedSensors
            ? "Set at least one live preview rate above 0 Hz."
            : null;

    public LiveDaqDetailViewModel(
        LiveDaqSnapshot snapshot,
        ILiveDaqSharedStream sharedStream,
        ILiveDaqCoordinator coordinator,
        IShellCoordinator shell,
        IDialogService dialogService,
        ILiveDaqKnownBoardsQuery knownBoardsQuery,
        ILiveDaqStore liveDaqStore,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider,
        IUiTimerScheduler uiTimerScheduler)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        sessionState = new LiveDaqSessionState(timeProvider);
        this.uiTimerScheduler = uiTimerScheduler;
        IdentityKey = snapshot.IdentityKey;
        BoardId = snapshot.BoardId;
        Endpoint = snapshot.Endpoint;
        SetupName = snapshot.SetupName;
        BikeName = snapshot.BikeName;
        Name = snapshot.DisplayName;
        this.sharedStream = sharedStream;
        this.coordinator = coordinator;
        this.knownBoardsQuery = knownBoardsQuery;
        this.liveDaqStore = liveDaqStore;
        streamLease = (sharedStream.AcquireLease() as LiveStreamLeaseResult.Acquired)?.Lease;
        ApplyRequestedRates(sharedStream.RequestedConfiguration);
        RefreshTravelCalibration();
        RefreshSharedStreamState();
        RefreshSnapshot();
        managementAvailabilitySubscription = coordinator.ObserveManagementAvailability(IdentityKey).Subscribe(availability =>
            UiThreadDispatcher.Post(() =>
            {
                managementAvailability = availability;
                Endpoint = availability.Device?.Endpoint;
                RefreshConnectAvailability();
                RefreshManagementAvailability();
            }));
    }

    [RelayCommand]
    private Task Loaded()
    {
        logger.Debug(
            "Live DAQ detail loaded for {IdentityKey} {Endpoint}",
            IdentityKey,
            Endpoint);

        hasLoaded = true;
        if (IsTabActive)
        {
            StartForegroundUpdates();
        }

        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task Unloaded()
    {
        logger.Debug(
            "Live DAQ detail unloaded for {IdentityKey} {Endpoint}; disconnect already in progress: {DisconnectInProgress}",
            IdentityKey,
            Endpoint,
            sharedStream.CurrentState.ConnectionState is LiveConnectionState.Disconnecting);

        Deactivate();
        return Task.CompletedTask;
    }

    protected override async Task CloseImplementation()
    {
        Deactivate();
        managementAvailabilitySubscription.Dispose();
        if (streamLease is not null)
        {
            await streamLease.DisposeAsync();
            streamLease = null;
        }
    }

    protected override bool CanSave() => false;
    protected override bool CanReset() => false;
    protected override bool CanExport() => false;

    private void Deactivate()
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

    [RelayCommand(CanExecute = nameof(CanStartSession))]
    private System.Threading.Tasks.Task StartSession()
    {
        shell.OpenEditor(new EditorKey.LiveSession(IdentityKey));
        return System.Threading.Tasks.Task.CompletedTask;
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task SetTime(CancellationToken cancellationToken)
    {
        if (!CanManage) return;
        using var linked = BeginManagementOperation(cancellationToken);
        try
        {
            var result = await coordinator.SetTimeAsync(IdentityKey, linked.Token);
            if (linked.IsCancellationRequested) return;
            if (result is LiveDaqManagementResult.TimeSet ok)
                Notifications.Add(FormattableString.Invariant($"Device time updated ({ok.RoundTripTime.TotalMilliseconds:0} ms RTT)."));
            else PresentManagementOutcome(result);
        }
        catch (OperationCanceledException) { }
        finally { EndManagementOperation(); }
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task SelectConfigFile(CancellationToken cancellationToken)
    {
        if (!CanManage) return;
        using var linked = BeginManagementOperation(cancellationToken);
        try
        {
            var result = await coordinator.SelectConfigFileAsync(linked.Token);
            if (linked.IsCancellationRequested) return;
            if (result is LiveDaqManagementResult.ConfigSelected selected)
            {
                SetPendingConfig(selected.File);
            }
            else PresentManagementOutcome(result);
        }
        catch (OperationCanceledException) { }
        finally { EndManagementOperation(); }
    }

    [RelayCommand(CanExecute = nameof(CanUploadConfig))]
    private async Task UploadConfig(CancellationToken cancellationToken)
    {
        if (!CanUploadConfig) return;
        if (pendingConfig?.Bytes is not { Length: > 0 } bytes) return;
        using var linked = BeginManagementOperation(cancellationToken);
        try
        {
            var result = await coordinator.WriteConfigAsync(IdentityKey, bytes, linked.Token);
            if (linked.IsCancellationRequested) return;
            if (result is LiveDaqManagementResult.ConfigWritten) Notifications.Add("CONFIG uploaded.");
            else PresentManagementOutcome(result);
        }
        catch (OperationCanceledException) { }
        finally
        {
            SetPendingConfig(null);
            EndManagementOperation();
        }
    }

    [RelayCommand(CanExecute = nameof(CanManage))]
    private async Task EditConfig(CancellationToken cancellationToken)
    {
        if (!CanManage) return;
        LiveDaqManagementResult result;
        using (var linked = BeginManagementOperation(cancellationToken))
        {
            try
            {
                result = await coordinator.ReadConfigAsync(IdentityKey, linked.Token);
                if (linked.IsCancellationRequested) return;
            }
            catch (OperationCanceledException) { return; }
            finally { EndManagementOperation(); }
        }
        if (result is not LiveDaqManagementResult.ConfigRead read)
        {
            PresentManagementOutcome(result);
            return;
        }
        var editor = new LiveDaqConfigEditorViewModel(read.Document, UploadEditedConfigAsync);
        await dialogService.ShowContentDialogAsync(editor, new DialogOptions(
            Title: "Edit CONFIG", Width: 640, Height: 720, MinWidth: 420, MinHeight: 520,
            CanResize: true, OverlayMaxWidth: 680, OverlayMaxHeight: 760));
    }

    private async Task<DaqManagementResult> UploadEditedConfigAsync(byte[] configBytes, CancellationToken cancellationToken)
    {
        var result = await coordinator.WriteConfigAsync(IdentityKey, configBytes, cancellationToken);
        switch (result)
        {
            case LiveDaqManagementResult.ConfigWritten:
                Notifications.Add("CONFIG uploaded.");
                return new DaqManagementResult.Ok();
            case LiveDaqManagementResult.Rejected rejected:
                return new DaqManagementResult.Error(rejected.ErrorCode, rejected.Message);
            case LiveDaqManagementResult.Busy busy:
                return new DaqManagementResult.Error(DaqManagementErrorCode.Busy, LiveDaqMessages.Busy(busy.Holder));
            case LiveDaqManagementResult.Unavailable unavailable:
                return new DaqManagementResult.Error(DaqManagementErrorCode.UnsupportedTarget, UnavailableMessage(unavailable.Reason));
            case LiveDaqManagementResult.Failed failed:
                var presentation = FailurePresentation.From(failed.Reason, failed.Details);
                ErrorMessages.Add(presentation);
                return new DaqManagementResult.Error(DaqManagementErrorCode.InternalError, presentation.Summary);
            default: throw new InvalidOperationException("Unexpected CONFIG write outcome.");
        }
    }

    private void PresentManagementOutcome(LiveDaqManagementResult result)
    {
        switch (result)
        {
            case LiveDaqManagementResult.Failed failed:
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
            case LiveDaqManagementResult.Rejected rejected:
                ErrorMessages.Add(new FailurePresentation(rejected.Message));
                break;
            case LiveDaqManagementResult.Busy busy:
                Notifications.Add(LiveDaqMessages.Busy(busy.Holder));
                break;
            case LiveDaqManagementResult.Unavailable unavailable:
                Notifications.Add(UnavailableMessage(unavailable.Reason));
                break;
        }
    }

    private static string UnavailableMessage(DaqManagementUnavailability reason) => reason switch
    {
        DaqManagementUnavailability.Offline => "DAQ is offline.",
        DaqManagementUnavailability.Unsupported => "Management is not supported by this DAQ.",
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };

    private async Task ConnectImplementationAsync()
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

            var result = await sharedStream.ConnectLiveAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var updatedState = sharedStream.CurrentState;
            if (result is LivePreviewStartResult.Busy busy)
            {
                Notifications.Add(LiveDaqMessages.Busy(busy.Holder));
                return;
            }
            if (result is LivePreviewStartResult.Unavailable unavailable)
                Notifications.Add(unavailable.Message);
            else if (result is LivePreviewStartResult.Started started && updatedState.ConnectionState is LiveConnectionState.Connected)
            {
                AddMissingSensorNotification(started.Header.MissingSensorMask);
                logger.Information(
                    "Live DAQ preview connected for {IdentityKey} {BoardId} {Endpoint}",
                    IdentityKey,
                    BoardId,
                    Endpoint);
            }

        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            // The shared stream owns failure state and logging.
        }

        RefreshSharedStreamState();
    }

    private async Task DisconnectImplementationAsync()
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
            if (await sharedStream.StopAsync() == LiveDisconnectResult.Locked)
            {
                Notifications.Add("DAQ is recording a live session. Close the capture tab before disconnecting.");
                return;
            }
            logger.Information(
                "Live DAQ preview disconnected for {IdentityKey} {BoardId} {Endpoint}",
                IdentityKey,
                BoardId,
                Endpoint);
        }
        catch (Exception)
        {
            // The shared stream owns failure state and logging.
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
        sessionState.ApplySharedSessionState(state.SessionHeader);
        Snapshot = sessionState.CreateSnapshot(state.ConnectionState);
    }

    partial void OnSnapshotChanged(LiveDaqUiSnapshot value)
    {
        RefreshManagementAvailability();
    }

    partial void OnIsManagementBusyChanged(bool value) => RefreshManagementAvailability();


    private void RequestSharedStreamRefresh()
    {
        if (UiThreadDispatcher.CheckAccess())
        {
            RefreshSharedStreamState();
            return;
        }

        UiThreadDispatcher.Post(RefreshSharedStreamState);
    }

    private FailurePresentation? shownConnectionFailure;

    private void RefreshSharedStreamState()
    {
        var state = sharedStream.CurrentState;
        var failure = LiveDaqMessages.ConnectionFailure(state.ConnectionState);
        if (shownConnectionFailure != failure)
        {
            shownConnectionFailure = failure;
            if (failure is not null) ErrorMessages.Add(failure);
        }
        sessionState.ApplySharedSessionState(state.SessionHeader);
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
        OnPropertyChanged(nameof(AreRequestedRatesEnabled));
        NotifySessionAvailability();
        OnPropertyChanged(nameof(ConnectDisabledTooltip));
        RefreshSnapshot();
    }

    private void RequestTravelProjectionRefresh()
    {
        if (UiThreadDispatcher.CheckAccess())
        {
            RefreshTravelCalibration();
            return;
        }

        UiThreadDispatcher.Post(RefreshTravelCalibration);
    }

    private void RequestSessionAvailabilityRefresh()
    {
        if (UiThreadDispatcher.CheckAccess())
        {
            NotifySessionAvailability();
            return;
        }

        UiThreadDispatcher.Post(NotifySessionAvailability);
    }

    private void RefreshTravelCalibration()
    {
        travelCalibration = knownBoardsQuery.GetTravelCalibration(IdentityKey);
        OnPropertyChanged(nameof(FrontTravelText));
        OnPropertyChanged(nameof(RearTravelText));
    }

    private void RequestHeaderRefresh()
    {
        if (UiThreadDispatcher.CheckAccess())
        {
            RefreshHeaderFromStore();
            return;
        }

        UiThreadDispatcher.Post(RefreshHeaderFromStore);
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

    }

    private void NotifySessionAvailability()
    {
        OnPropertyChanged(nameof(CanStartSession));
        StartSessionCommand.NotifyCanExecuteChanged();
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

    private void SetPendingConfig(SelectedDeviceConfigFile? file)
    {
        pendingConfig = file;
        OnPropertyChanged(nameof(HasPendingConfig));
        OnPropertyChanged(nameof(PendingConfigFileName));
        RefreshManagementAvailability();
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
        OnPropertyChanged(nameof(CanConnect));
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

    private void StartForegroundUpdates()
    {
        if (sharedStream.CurrentState.IsClosed)
        {
            RefreshSharedStreamState();
            RefreshSnapshot();
            return;
        }

        // Frames arrive far faster than the UI needs to repaint. The session state
        // accumulates every frame, and this timer snapshots it at a fixed cadence.
        uiRefreshTimer ??= uiTimerScheduler.SchedulePeriodic(TimeSpan.FromMilliseconds(100), RefreshSnapshot);
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
        uiRefreshTimer?.Dispose();
        uiRefreshTimer = null;
        DisposeScopedSubscriptions();
    }
}

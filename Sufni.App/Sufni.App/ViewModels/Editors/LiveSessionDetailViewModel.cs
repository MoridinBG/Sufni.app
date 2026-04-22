using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Models;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

public sealed partial class LiveSessionDetailViewModel : TabPageViewModelBase,
    ISessionStatisticsWorkspace,
    ISessionSidebarWorkspace,
    ILiveSessionControlsWorkspace
{
    private readonly LiveSessionGraphWorkspaceViewModel graphWorkspace;
    private readonly LiveSessionMediaWorkspaceViewModel mediaWorkspace;
    private readonly ILiveSessionService? liveSessionService;
    private readonly ISessionCoordinator? sessionCoordinator;
    private readonly DispatcherTimer uiRefreshTimer;
    private readonly object presentationGate = new();
    private bool hasLoaded;
    private long? blockedSavedCaptureRevision;
    private LiveSessionPresentationSnapshot pendingPresentation = LiveSessionPresentationSnapshot.Empty;
    private bool hasPendingPresentation;

    public string IdentityKey { get; }
    public Guid SetupId { get; }
    public string? SetupName { get; }
    public Guid BikeId { get; }
    public string? BikeName { get; }

    public ILiveSessionGraphWorkspace GraphWorkspace => graphWorkspace;
    public ISessionMediaWorkspace MediaWorkspace => mediaWorkspace;
    public SuspensionSettings ForkSettings { get; } = new();
    public SuspensionSettings ShockSettings { get; } = new();

    [ObservableProperty]
    private string? descriptionText;

    [ObservableProperty]
    private TelemetryData? telemetryData;

    [ObservableProperty]
    private SessionDamperPercentages damperPercentages = new(null, null, null, null, null, null, null, null);

    [ObservableProperty]
    private LiveSessionControlState controlState = LiveSessionControlState.Empty;

    public bool HasFrontStatistics => TelemetryData?.HasStrokeData(SuspensionType.Front) == true;
    public bool HasRearStatistics => TelemetryData?.HasStrokeData(SuspensionType.Rear) == true;
    public bool HasCompressionBalanceTelemetry => TelemetryData?.HasBalanceData(BalanceType.Compression) == true;
    public bool HasReboundBalanceTelemetry => TelemetryData?.HasBalanceData(BalanceType.Rebound) == true;

    public LiveSessionDetailViewModel()
    {
        IdentityKey = string.Empty;
        graphWorkspace = new LiveSessionGraphWorkspaceViewModel();
        mediaWorkspace = new LiveSessionMediaWorkspaceViewModel();
        uiRefreshTimer = CreateUiRefreshTimer();
        Name = CreateDefaultName(DateTimeOffset.Now);
    }

    public LiveSessionDetailViewModel(
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService,
        ISessionCoordinator sessionCoordinator,
        ITileLayerService tileLayerService,
        IShellCoordinator shell,
        IDialogService dialogService)
        : base(shell, dialogService)
    {
        IdentityKey = context.IdentityKey;
        SetupId = context.SetupId;
        SetupName = context.SetupName;
        BikeId = context.BikeId;
        BikeName = context.BikeName;
        this.liveSessionService = liveSessionService;
        this.sessionCoordinator = sessionCoordinator;

        var timeline = new SessionTimelineLinkViewModel();
        graphWorkspace = new LiveSessionGraphWorkspaceViewModel(timeline, CreatePlotRanges(context), liveSessionService.GraphBatches);
        mediaWorkspace = new LiveSessionMediaWorkspaceViewModel(tileLayerService, dialogService, timeline);
        uiRefreshTimer = CreateUiRefreshTimer();
        Name = CreateDefaultName(DateTimeOffset.Now);

        ApplyPresentation(liveSessionService.Current);

        SaveCommand.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SaveCommand.IsRunning))
            {
                RefreshCommandState();
            }
        };
        ResetCommand.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ResetCommand.IsRunning))
            {
                RefreshCommandState();
            }
        };
    }

    partial void OnTelemetryDataChanged(TelemetryData? value)
    {
        OnPropertyChanged(nameof(HasFrontStatistics));
        OnPropertyChanged(nameof(HasRearStatistics));
        OnPropertyChanged(nameof(HasCompressionBalanceTelemetry));
        OnPropertyChanged(nameof(HasReboundBalanceTelemetry));
    }

    partial void OnDescriptionTextChanged(string? value)
    {
    }

    [RelayCommand]
    private async Task Loaded()
    {
        uiRefreshTimer.Start();
        RefreshUi();

        if (liveSessionService is not null)
        {
            EnsureScopedSubscription(disposables =>
                disposables.Add(liveSessionService.Snapshots.Subscribe(QueuePresentationRefresh)));
        }

        if (hasLoaded)
        {
            return;
        }

        hasLoaded = true;
        await mediaWorkspace.InitializeAsync();

        if (liveSessionService is null)
        {
            return;
        }

        await liveSessionService.EnsureAttachedAsync();
        ApplyPresentation(liveSessionService.Current);
    }

    [RelayCommand]
    private async Task Unloaded()
    {
        uiRefreshTimer.Stop();
        DisposeScopedSubscriptions();
        await Task.CompletedTask;
    }

    protected override async Task CloseImplementation()
    {
        uiRefreshTimer.Stop();

        if (liveSessionService is not null)
        {
            await liveSessionService.DisposeAsync();
        }
    }

    protected override void EvaluateDirtiness()
    {
        IsDirty = ControlState.CanSave && !IsCurrentCaptureAlreadySaved();
    }

    protected override bool CanSave()
    {
        return ControlState.CanSave && !IsCurrentCaptureAlreadySaved() && !SaveCommand.IsRunning && !ResetCommand.IsRunning;
    }

    protected override bool CanReset()
    {
        return ControlState.CanSave && !SaveCommand.IsRunning && !ResetCommand.IsRunning;
    }

    protected override async Task ResetImplementation()
    {
        if (liveSessionService is null)
        {
            return;
        }

        await liveSessionService.ResetCaptureAsync();
        ResetCapturePresentation();
    }

    protected override async Task SaveImplementation()
    {
        if (liveSessionService is null || sessionCoordinator is null)
        {
            return;
        }

        var shouldRefreshAutoName = string.IsNullOrWhiteSpace(Name) || IsAutoGeneratedName(Name);

        try
        {
            var capture = await liveSessionService.PrepareCaptureForSaveAsync();
            var savedCaptureRevision = liveSessionService.Current.CaptureRevision;
            var saveTime = DateTimeOffset.Now;
            var sessionName = shouldRefreshAutoName ? CreateDefaultName(saveTime) : Name!;
            var session = new Session(
                id: Guid.NewGuid(),
                name: sessionName,
                description: DescriptionText ?? string.Empty,
                setup: SetupId,
                timestamp: capture.TelemetryCapture.Metadata.Timestamp)
            {
                FrontSpringRate = ForkSettings.SpringRate,
                FrontHighSpeedCompression = ForkSettings.HighSpeedCompression,
                FrontLowSpeedCompression = ForkSettings.LowSpeedCompression,
                FrontLowSpeedRebound = ForkSettings.LowSpeedRebound,
                FrontHighSpeedRebound = ForkSettings.HighSpeedRebound,
                RearSpringRate = ShockSettings.SpringRate,
                RearHighSpeedCompression = ShockSettings.HighSpeedCompression,
                RearLowSpeedCompression = ShockSettings.LowSpeedCompression,
                RearLowSpeedRebound = ShockSettings.LowSpeedRebound,
                RearHighSpeedRebound = ShockSettings.HighSpeedRebound,
            };

            var result = await sessionCoordinator.SaveLiveCaptureAsync(session, capture);
            switch (result)
            {
                case LiveSessionSaveResult.Saved saved:
                    blockedSavedCaptureRevision = savedCaptureRevision;

                    try
                    {
                        await liveSessionService.ResetCaptureAsync();
                        ResetCapturePresentation();
                        if (shouldRefreshAutoName)
                        {
                            Name = CreateDefaultName(DateTimeOffset.Now);
                        }

                        await sessionCoordinator.OpenEditAsync(saved.SessionId);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        ErrorMessages.Add($"Live session was saved, but post-save cleanup failed: {e.Message}");
                    }
                    break;

                case LiveSessionSaveResult.Failed failed:
                    ErrorMessages.Add($"Live session could not be saved: {failed.ErrorMessage}");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            ErrorMessages.Add($"Live session could not be saved: {e.Message}");
        }
        finally
        {
            Dispatcher.UIThread.Post(() =>
            {
                EvaluateDirtiness();
                RefreshCommandState();
            }, DispatcherPriority.Background);
        }
    }

    private static string CreateDefaultName(DateTimeOffset localTime)
    {
        return $"Live Session {localTime.LocalDateTime:dd-MM-yyyy HH:mm:ss}";
    }

    private static bool IsAutoGeneratedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("Live Session ", StringComparison.Ordinal))
        {
            return false;
        }

        return DateTime.TryParseExact(
            name["Live Session ".Length..],
            "dd-MM-yyyy HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out _);
    }

    private static LiveSessionPlotRanges CreatePlotRanges(LiveDaqSessionContext context)
    {
        var travelMaximum = Math.Max(
            context.TravelCalibration.Front?.MaxTravel ?? 0,
            context.TravelCalibration.Rear?.MaxTravel ?? 0);

        if (travelMaximum <= 0)
        {
            travelMaximum = Math.Max(
                context.BikeData.FrontMaxTravel ?? 0,
                context.BikeData.RearMaxTravel ?? 0);
        }

        return new LiveSessionPlotRanges(
            TravelMaximum: Math.Max(1, travelMaximum),
            VelocityMaximum: 5,
            ImuMaximum: 5);
    }

    private void QueuePresentationRefresh(LiveSessionPresentationSnapshot snapshot)
    {
        lock (presentationGate)
        {
            pendingPresentation = snapshot;
            hasPendingPresentation = true;
        }
    }

    private void ApplyPresentation(LiveSessionPresentationSnapshot snapshot)
    {
        if (blockedSavedCaptureRevision != snapshot.CaptureRevision)
        {
            blockedSavedCaptureRevision = null;
        }

        TelemetryData = snapshot.StatisticsTelemetry;
        DamperPercentages = snapshot.DamperPercentages;
        mediaWorkspace.SetTrackPoints(snapshot.SessionTrackPoints);

        if (snapshot.Controls.SessionHeader is { } header)
        {
            Timestamp = header.SessionStartUtc.LocalDateTime;
        }

        ControlState = ApplyControlFlags(snapshot.Controls);
        EvaluateDirtiness();
        RefreshCommandState();
    }

    private bool IsCurrentCaptureAlreadySaved()
    {
        return blockedSavedCaptureRevision is not null;
    }

    private LiveSessionControlState ApplyControlFlags(LiveSessionControlState controlState)
    {
        var captureDuration = RefreshCaptureDuration(controlState.CaptureStartUtc) ?? controlState.CaptureDuration;
        return controlState with
        {
            CaptureDuration = captureDuration,
        };
    }

    private LiveSessionControlState RefreshControlState()
    {
        ControlState = ApplyControlFlags(ControlState);
        EvaluateDirtiness();
        return ControlState;
    }

    private void RefreshUi()
    {
        ApplyPendingPresentation();
        RefreshControlState();
    }

    private void ApplyPendingPresentation()
    {
        LiveSessionPresentationSnapshot snapshot;

        lock (presentationGate)
        {
            if (!hasPendingPresentation)
            {
                return;
            }

            snapshot = pendingPresentation;
            hasPendingPresentation = false;
        }

        ApplyPresentation(snapshot);
    }

    private void RefreshCommandState()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    private void ResetCapturePresentation()
    {
        graphWorkspace.Timeline.Reset();
        ApplyPresentation(liveSessionService!.Current);
    }

    // Live session updates arrive far faster than the controls need to repaint.
    // Keep the latest snapshot and project it into the UI at a fixed cadence.
    private DispatcherTimer CreateUiRefreshTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(LiveSessionRefreshCadence.UiRefreshIntervalMs)
        };
        timer.Tick += (_, _) => RefreshUi();
        return timer;
    }

    private static TimeSpan? RefreshCaptureDuration(DateTimeOffset? captureStartUtc)
    {
        if (captureStartUtc is null)
        {
            return null;
        }

        var duration = DateTimeOffset.UtcNow - captureStartUtc.Value;
        return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
    }
}
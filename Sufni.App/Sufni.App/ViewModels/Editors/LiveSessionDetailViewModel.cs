using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Models;
using Sufni.App.Coordinators;
using Sufni.App.Presentation;
using Sufni.App.Queries;
using Sufni.App.SessionGraphs;
using Sufni.App.SessionDetails;
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
    private readonly ILiveSessionService liveSessionService;
    private readonly SessionCoordinator sessionCoordinator;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly DispatcherTimer uiRefreshTimer;
    private readonly object presentationGate = new();
    private readonly object graphBatchRefreshGate = new();
    private bool hasLoaded;
    private long? blockedSavedCaptureRevision;
    private LiveSessionPresentationSnapshot pendingPresentation = LiveSessionPresentationSnapshot.Empty;
    private bool hasPendingPresentation;
    private GraphBatchPresence pendingGraphBatchPresence;
    private bool hasPendingGraphBatchRefresh;
    private readonly bool hasFrontTravelCalibration;
    private readonly bool hasRearTravelCalibration;
    private SessionPresentationDimensions? lastPresentationDimensions;
    private TelemetryData? lastBakedTelemetryData;
    private CancellationTokenSource? bakeCts;

    public string IdentityKey { get; }
    public Guid SetupId { get; }
    public string? SetupName { get; }
    public Guid BikeId { get; }
    public string? BikeName { get; }

    public ILiveSessionGraphWorkspace GraphWorkspace => graphWorkspace;
    public ISessionMediaWorkspace MediaWorkspace => mediaWorkspace;
    public NotesPageViewModel NotesPage { get; } = new();
    public SpringPageViewModel SpringPage { get; } = new();
    public DamperPageViewModel DamperPage { get; } = new();
    public BalancePageViewModel BalancePage { get; } = new();
    public LiveGraphPageViewModel LiveGraphPage { get; }
    public ObservableCollection<PageViewModelBase> Pages { get; }

    public SuspensionSettings ForkSettings => NotesPage.ForkSettings;
    public SuspensionSettings ShockSettings => NotesPage.ShockSettings;

    public string? DescriptionText
    {
        get => NotesPage.Description;
        set => NotesPage.Description = value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrontStatisticsState))]
    [NotifyPropertyChangedFor(nameof(RearStatisticsState))]
    [NotifyPropertyChangedFor(nameof(CompressionBalanceState))]
    [NotifyPropertyChangedFor(nameof(ReboundBalanceState))]
    private TelemetryData? telemetryData;

    [ObservableProperty]
    private SessionDamperPercentages damperPercentages = new(null, null, null, null, null, null, null, null);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrontStatisticsState))]
    [NotifyPropertyChangedFor(nameof(RearStatisticsState))]
    [NotifyPropertyChangedFor(nameof(CompressionBalanceState))]
    [NotifyPropertyChangedFor(nameof(ReboundBalanceState))]
    private LiveSessionControlState controlState = LiveSessionControlState.Empty;

    [ObservableProperty]
    private SessionScreenPresentationState screenState = SessionScreenPresentationState.Ready;

    public SurfacePresentationState FrontStatisticsState => CreateStatisticsState(ControlState.SessionHeader, TelemetryData, SuspensionType.Front, hasFrontTravelCalibration);
    public SurfacePresentationState RearStatisticsState => CreateStatisticsState(ControlState.SessionHeader, TelemetryData, SuspensionType.Rear, hasRearTravelCalibration);
    public SurfacePresentationState CompressionBalanceState => CreateBalanceState(ControlState.SessionHeader, TelemetryData, hasFrontTravelCalibration, hasRearTravelCalibration, BalanceType.Compression);
    public SurfacePresentationState ReboundBalanceState => CreateBalanceState(ControlState.SessionHeader, TelemetryData, hasFrontTravelCalibration, hasRearTravelCalibration, BalanceType.Rebound);
    public SurfacePresentationState FrontForkVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState FrontFrameVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState RearForkVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState RearFrameVibrationState => SurfacePresentationState.Hidden;

    public LiveSessionDetailViewModel(
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService,
        SessionCoordinator sessionCoordinator,
        ISessionPresentationService sessionPresentationService,
        IBackgroundTaskRunner backgroundTaskRunner,
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
        this.sessionPresentationService = sessionPresentationService;
        this.backgroundTaskRunner = backgroundTaskRunner;
        hasFrontTravelCalibration = context.TravelCalibration.Front is not null;
        hasRearTravelCalibration = context.TravelCalibration.Rear is not null;

        var timeline = new SessionTimelineLinkViewModel();
        graphWorkspace = new LiveSessionGraphWorkspaceViewModel(timeline, CreatePlotRanges(context), liveSessionService.GraphBatches);
        mediaWorkspace = new LiveSessionMediaWorkspaceViewModel(tileLayerService, dialogService, timeline);
        uiRefreshTimer = CreateUiRefreshTimer();
        Name = CreateDefaultName(DateTimeOffset.Now);
        LiveGraphPage = new LiveGraphPageViewModel(graphWorkspace, mediaWorkspace);
        Pages = [LiveGraphPage, SpringPage, DamperPage, NotesPage];
        WireNotesPageForwarding();

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

    [RelayCommand]
    private async Task Loaded(Rect? bounds = null)
    {
        var dimensions = CreatePresentationDimensions(bounds);
        if (dimensions is not null)
        {
            lastPresentationDimensions = dimensions;
        }

        uiRefreshTimer.Start();
        RefreshUi();

        EnsureScopedSubscription(disposables =>
        {
            disposables.Add(liveSessionService.Snapshots.Subscribe(QueuePresentationRefresh));
            disposables.Add(liveSessionService.GraphBatches.Subscribe(QueueGraphBatchRefresh));
        });

        if (hasLoaded)
        {
            return;
        }

        hasLoaded = true;
        await mediaWorkspace.InitializeAsync();

        await liveSessionService.EnsureAttachedAsync();
        ApplyPresentation(liveSessionService.Current);
    }

    [RelayCommand]
    private async Task Unloaded()
    {
        uiRefreshTimer.Stop();
        CancelBake();
        DisposeScopedSubscriptions();
        await Task.CompletedTask;
    }

    private static SessionPresentationDimensions? CreatePresentationDimensions(Rect? bounds)
    {
        if (bounds is not Rect rect || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        return new SessionPresentationDimensions((int)rect.Width, (int)(rect.Height / 2.0));
    }

    protected override async Task CloseImplementation()
    {
        uiRefreshTimer.Stop();
        CancelBake();

        await liveSessionService.DisposeAsync();
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
        await liveSessionService.ResetCaptureAsync();
        ResetCapturePresentation();
    }

    protected override async Task SaveImplementation()
    {
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

    private void EnsureBalancePage(bool balanceAvailable)
    {
        var containsBalancePage = Pages.Contains(BalancePage);
        if (balanceAvailable)
        {
            if (containsBalancePage)
            {
                return;
            }

            var notesIndex = Pages.IndexOf(NotesPage);
            if (notesIndex < 0)
            {
                Pages.Add(BalancePage);
            }
            else
            {
                Pages.Insert(notesIndex, BalancePage);
            }

            return;
        }

        if (containsBalancePage)
        {
            Pages.Remove(BalancePage);
        }
    }

    private void WireNotesPageForwarding()
    {
        NotesPage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(NotesPageViewModel.Description))
            {
                OnPropertyChanged(nameof(DescriptionText));
            }

            EvaluateDirtiness();
            RefreshCommandState();
        };
        NotesPage.ForkSettings.PropertyChanged += (_, _) =>
        {
            EvaluateDirtiness();
            RefreshCommandState();
        };
        NotesPage.ShockSettings.PropertyChanged += (_, _) =>
        {
            EvaluateDirtiness();
            RefreshCommandState();
        };
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

    private static SurfacePresentationState CreateStatisticsState(
        LiveSessionHeader? sessionHeader,
        TelemetryData? telemetryData,
        SuspensionType suspensionType,
        bool sideConfigured)
    {
        if (!sideConfigured || sessionHeader is not { AcceptedTravelHz: > 0 })
        {
            return SurfacePresentationState.Hidden;
        }

        if (telemetryData is null)
        {
            return SurfacePresentationState.WaitingForData("Waiting for statistics.");
        }

        var suspension = suspensionType == SuspensionType.Front ? telemetryData.Front : telemetryData.Rear;
        if (!suspension.Present)
        {
            return SurfacePresentationState.WaitingForData("Waiting for statistics.");
        }

        return telemetryData.HasStrokeData(suspensionType)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.WaitingForData("Waiting for statistics.");
    }

    private static SurfacePresentationState CreateBalanceState(
        LiveSessionHeader? sessionHeader,
        TelemetryData? telemetryData,
        bool frontConfigured,
        bool rearConfigured,
        BalanceType balanceType)
    {
        if (!frontConfigured || !rearConfigured || sessionHeader is not { AcceptedTravelHz: > 0 })
        {
            return SurfacePresentationState.Hidden;
        }

        if (telemetryData is null || !telemetryData.Front.Present || !telemetryData.Rear.Present)
        {
            return SurfacePresentationState.WaitingForData("Waiting for balance data.");
        }

        return telemetryData.HasBalanceData(balanceType)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.WaitingForData("Waiting for balance data.");
    }

    private void QueueGraphBatchRefresh(LiveGraphBatch batch)
    {
        var presence = GraphBatchPresence.FromBatch(batch);
        if (!presence.HasAnyData)
        {
            return;
        }

        var shouldPost = false;
        lock (graphBatchRefreshGate)
        {
            pendingGraphBatchPresence = pendingGraphBatchPresence.Combine(presence);
            if (!hasPendingGraphBatchRefresh)
            {
                hasPendingGraphBatchRefresh = true;
                shouldPost = true;
            }
        }

        if (shouldPost)
        {
            Dispatcher.UIThread.Post(FlushGraphBatchRefresh, DispatcherPriority.Background);
        }
    }

    private void FlushGraphBatchRefresh()
    {
        GraphBatchPresence presence;
        lock (graphBatchRefreshGate)
        {
            presence = pendingGraphBatchPresence;
            pendingGraphBatchPresence = default;
            hasPendingGraphBatchRefresh = false;
        }

        if (presence.HasAnyData)
        {
            graphWorkspace.ApplyGraphDataPresence(presence.HasTravelData, presence.HasImuData);
        }
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

        graphWorkspace.ApplySessionHeader(snapshot.Controls.SessionHeader);
        mediaWorkspace.ApplySessionHeader(snapshot.Controls.SessionHeader);
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

        MaybeQueueBake(snapshot.StatisticsTelemetry);
    }

    private void MaybeQueueBake(TelemetryData? telemetryData)
    {
        if (telemetryData is null ||
            lastPresentationDimensions is not { } dimensions)
        {
            return;
        }

        if (ReferenceEquals(lastBakedTelemetryData, telemetryData))
        {
            return;
        }

        lastBakedTelemetryData = telemetryData;
        CancelBake();
        var cts = new CancellationTokenSource();
        bakeCts = cts;
        var service = sessionPresentationService;
        var runner = backgroundTaskRunner;

        _ = Task.Run(async () =>
        {
            try
            {
                var data = await runner.RunAsync(
                    () => service.BuildCachePresentation(telemetryData, dimensions, cts.Token),
                    cts.Token);

                if (cts.IsCancellationRequested)
                {
                    return;
                }

                Dispatcher.UIThread.Post(() =>
                {
                    if (cts.IsCancellationRequested)
                    {
                        return;
                    }

                    ApplyCachePresentation(data);
                }, DispatcherPriority.Background);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                Dispatcher.UIThread.Post(() =>
                    ErrorMessages.Add($"Live statistics render failed: {e.Message}"),
                    DispatcherPriority.Background);
            }
        });
    }

    private void CancelBake()
    {
        var cts = bakeCts;
        bakeCts = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        cts.Dispose();
    }

    private void ApplyCachePresentation(SessionCachePresentationData data)
    {
        var hasFrontTravelHistogram = !string.IsNullOrWhiteSpace(data.FrontTravelHistogram);
        var hasRearTravelHistogram = !string.IsNullOrWhiteSpace(data.RearTravelHistogram);
        var hasFrontVelocityHistogram = !string.IsNullOrWhiteSpace(data.FrontVelocityHistogram);
        var hasRearVelocityHistogram = !string.IsNullOrWhiteSpace(data.RearVelocityHistogram);
        var hasCompressionBalance = !string.IsNullOrWhiteSpace(data.CompressionBalance);
        var hasReboundBalance = !string.IsNullOrWhiteSpace(data.ReboundBalance);

        var frontStats = FrontStatisticsState;
        var rearStats = RearStatisticsState;
        var compressionBalance = CompressionBalanceState;
        var reboundBalance = ReboundBalanceState;

        SpringPage.FrontTravelHistogram = data.FrontTravelHistogram;
        SpringPage.RearTravelHistogram = data.RearTravelHistogram;
        SpringPage.FrontHistogramState = ResolveSurfaceState(hasFrontTravelHistogram, frontStats);
        SpringPage.RearHistogramState = ResolveSurfaceState(hasRearTravelHistogram, rearStats);

        DamperPage.FrontVelocityHistogram = data.FrontVelocityHistogram;
        DamperPage.RearVelocityHistogram = data.RearVelocityHistogram;
        DamperPage.FrontHistogramState = ResolveSurfaceState(hasFrontVelocityHistogram, frontStats);
        DamperPage.RearHistogramState = ResolveSurfaceState(hasRearVelocityHistogram, rearStats);

        DamperPage.FrontHscPercentage = data.DamperPercentages.FrontHscPercentage;
        DamperPage.RearHscPercentage = data.DamperPercentages.RearHscPercentage;
        DamperPage.FrontLscPercentage = data.DamperPercentages.FrontLscPercentage;
        DamperPage.RearLscPercentage = data.DamperPercentages.RearLscPercentage;
        DamperPage.FrontLsrPercentage = data.DamperPercentages.FrontLsrPercentage;
        DamperPage.RearLsrPercentage = data.DamperPercentages.RearLsrPercentage;
        DamperPage.FrontHsrPercentage = data.DamperPercentages.FrontHsrPercentage;
        DamperPage.RearHsrPercentage = data.DamperPercentages.RearHsrPercentage;
        DamperPercentages = data.DamperPercentages;

        BalancePage.CompressionBalance = data.CompressionBalance;
        BalancePage.ReboundBalance = data.ReboundBalance;
        BalancePage.CompressionBalanceState = ResolveSurfaceState(hasCompressionBalance, compressionBalance);
        BalancePage.ReboundBalanceState = ResolveSurfaceState(hasReboundBalance, reboundBalance);

        var balancePageVisible = data.BalanceAvailable
            || compressionBalance.ReservesLayout
            || reboundBalance.ReservesLayout;
        EnsureBalancePage(balancePageVisible);
    }

    private static SurfacePresentationState ResolveSurfaceState(bool svgPresent, SurfacePresentationState workspaceState)
    {
        if (svgPresent)
        {
            return SurfacePresentationState.Ready;
        }

        return workspaceState.IsHidden ? SurfacePresentationState.Hidden : workspaceState;
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
        CancelBake();
        lastBakedTelemetryData = null;
        ClearStatisticsPages();
        graphWorkspace.Timeline.Reset();
        ApplyPresentation(liveSessionService.Current);
    }

    private void ClearStatisticsPages()
    {
        SpringPage.FrontTravelHistogram = null;
        SpringPage.RearTravelHistogram = null;
        SpringPage.FrontHistogramState = SurfacePresentationState.Hidden;
        SpringPage.RearHistogramState = SurfacePresentationState.Hidden;

        DamperPage.FrontVelocityHistogram = null;
        DamperPage.RearVelocityHistogram = null;
        DamperPage.FrontHistogramState = SurfacePresentationState.Hidden;
        DamperPage.RearHistogramState = SurfacePresentationState.Hidden;
        DamperPage.FrontHscPercentage = null;
        DamperPage.RearHscPercentage = null;
        DamperPage.FrontLscPercentage = null;
        DamperPage.RearLscPercentage = null;
        DamperPage.FrontLsrPercentage = null;
        DamperPage.RearLsrPercentage = null;
        DamperPage.FrontHsrPercentage = null;
        DamperPage.RearHsrPercentage = null;

        BalancePage.CompressionBalance = null;
        BalancePage.ReboundBalance = null;
        BalancePage.CompressionBalanceState = SurfacePresentationState.Hidden;
        BalancePage.ReboundBalanceState = SurfacePresentationState.Hidden;

        DamperPercentages = new SessionDamperPercentages(null, null, null, null, null, null, null, null);
        EnsureBalancePage(balanceAvailable: false);
    }

    // Live session updates arrive far faster than the controls need to repaint.
    // Keep the latest snapshot and project it into the UI at a fixed cadence.
    private DispatcherTimer CreateUiRefreshTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(SessionGraphSettings.LiveUiRefreshIntervalMs)
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

    private readonly record struct GraphBatchPresence(bool HasTravelData, bool HasImuData)
    {
        public bool HasAnyData => HasTravelData || HasImuData;

        public static GraphBatchPresence FromBatch(LiveGraphBatch batch)
        {
            return new GraphBatchPresence(
                HasTravelData: batch.TravelTimes.Count > 0
                    || batch.FrontTravel.Count > 0
                    || batch.RearTravel.Count > 0
                    || batch.VelocityTimes.Count > 0
                    || batch.FrontVelocity.Count > 0
                    || batch.RearVelocity.Count > 0,
                HasImuData: HasAnyImuData(batch));
        }

        public GraphBatchPresence Combine(GraphBatchPresence other) => new(
            HasTravelData || other.HasTravelData,
            HasImuData || other.HasImuData);

        private static bool HasAnyImuData(LiveGraphBatch batch)
        {
            foreach (var series in batch.ImuTimes.Values)
            {
                if (series.Count > 0)
                {
                    return true;
                }
            }

            foreach (var series in batch.ImuMagnitudes.Values)
            {
                if (series.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}

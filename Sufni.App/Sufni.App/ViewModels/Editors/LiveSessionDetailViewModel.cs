using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
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
    ISessionShellMobileWorkspace,
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
    public PreferencesPageViewModel PreferencesPage { get; } = new();
    public SpringPageViewModel SpringPage { get; }
    public DamperPageViewModel DamperPage { get; }
    public BalancePageViewModel BalancePage { get; }
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
    private SessionDamperPercentages damperPercentages = SessionDamperPercentages.Empty;

    private TravelHistogramMode selectedTravelHistogramMode = TravelHistogramMode.ActiveSuspension;
    private BalanceDisplacementMode selectedBalanceDisplacementMode = BalanceDisplacementMode.Zenith;
    private BalanceSpeedMode selectedBalanceSpeedMode = BalanceSpeedMode.Both;
    private VelocityAverageMode selectedVelocityAverageMode = VelocityAverageMode.SampleAveraged;
    private SessionAnalysisTargetProfile selectedSessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Trail;

    public TravelHistogramMode SelectedTravelHistogramMode
    {
        get => selectedTravelHistogramMode;
        set
        {
            if (SetProperty(ref selectedTravelHistogramMode, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
            }
        }
    }

    public BalanceDisplacementMode SelectedBalanceDisplacementMode
    {
        get => selectedBalanceDisplacementMode;
        set
        {
            if (SetProperty(ref selectedBalanceDisplacementMode, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
            }
        }
    }

    public BalanceSpeedMode SelectedBalanceSpeedMode
    {
        get => selectedBalanceSpeedMode;
        set
        {
            if (SetProperty(ref selectedBalanceSpeedMode, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
            }
        }
    }

    public VelocityAverageMode SelectedVelocityAverageMode
    {
        get => selectedVelocityAverageMode;
        set
        {
            if (SetProperty(ref selectedVelocityAverageMode, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
            }
        }
    }

    public SessionAnalysisTargetProfile SelectedSessionAnalysisTargetProfile
    {
        get => selectedSessionAnalysisTargetProfile;
        set => SetProperty(ref selectedSessionAnalysisTargetProfile, value);
    }

    public IReadOnlyList<TravelHistogramModeOption> TravelHistogramModeOptions { get; } = SessionAnalysisPresentation.TravelHistogramModeOptions;
    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } = SessionAnalysisPresentation.BalanceDisplacementModeOptions;
    public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } = SessionAnalysisPresentation.BalanceSpeedModeOptions;
    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } = SessionAnalysisPresentation.VelocityAverageModeOptions;
    public IReadOnlyList<SessionAnalysisTargetProfileOption> SessionAnalysisTargetProfileOptions { get; } = SessionAnalysisPresentation.SessionAnalysisTargetProfileOptions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrontStatisticsState))]
    [NotifyPropertyChangedFor(nameof(RearStatisticsState))]
    [NotifyPropertyChangedFor(nameof(CompressionBalanceState))]
    [NotifyPropertyChangedFor(nameof(ReboundBalanceState))]
    private LiveSessionControlState controlState = LiveSessionControlState.Empty;

    [ObservableProperty]
    private SessionScreenPresentationState screenState = SessionScreenPresentationState.Ready;

    public SurfacePresentationState FrontStatisticsState => SessionStatisticsSurfaceState.ForSuspension(IsTravelStatisticsExpected(hasFrontTravelCalibration), TelemetryData, SuspensionType.Front);
    public SurfacePresentationState RearStatisticsState => SessionStatisticsSurfaceState.ForSuspension(IsTravelStatisticsExpected(hasRearTravelCalibration), TelemetryData, SuspensionType.Rear);
    public SurfacePresentationState CompressionBalanceState => SessionStatisticsSurfaceState.ForBalance(IsBalanceStatisticsExpected(), TelemetryData, BalanceType.Compression);
    public SurfacePresentationState ReboundBalanceState => SessionStatisticsSurfaceState.ForBalance(IsBalanceStatisticsExpected(), TelemetryData, BalanceType.Rebound);
    public SurfacePresentationState FrontForkVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState FrontFrameVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState RearForkVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState RearFrameVibrationState => SurfacePresentationState.Hidden;
    public TelemetryTimeRange? AnalysisRange => null;
    public SessionAnalysisResult SessionAnalysis => SessionAnalysisResult.Hidden;
    public string SessionAnalysisRangeText => "Live session";
    public string SessionAnalysisModesText => SessionAnalysisPresentation.DescribeModes(
        SelectedTravelHistogramMode,
        SelectedVelocityAverageMode,
        SelectedBalanceDisplacementMode,
        SelectedBalanceSpeedMode);

    public LiveSessionDetailViewModel(
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService,
        SessionCoordinator sessionCoordinator,
        ISessionPresentationService sessionPresentationService,
        IBackgroundTaskRunner backgroundTaskRunner,
        ITileLayerService tileLayerService,
        IShellCoordinator shell,
        IDialogService dialogService,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(shell, dialogService, uiThreadDispatcher)
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
        mediaWorkspace = new LiveSessionMediaWorkspaceViewModel(tileLayerService, dialogService, timeline, uiThreadDispatcher);
        uiRefreshTimer = CreateUiRefreshTimer();
        Name = CreateDefaultName(DateTimeOffset.Now);
        LiveGraphPage = new LiveGraphPageViewModel(graphWorkspace, mediaWorkspace);
        SpringPage = new SpringPageViewModel(this);
        DamperPage = new DamperPageViewModel(this);
        BalancePage = new BalancePageViewModel(this);
        Pages = [LiveGraphPage, SpringPage, DamperPage, NotesPage, PreferencesPage];
        WireNotesPageForwarding();
        WireRuntimePreferenceForwarding();

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
        mediaWorkspace.Dispose();
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

            var result = await sessionCoordinator.SaveLiveCaptureAsync(session, capture, CreateCurrentSessionPreferences());
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

    private void WireRuntimePreferenceForwarding()
    {
        PreferencesPage.TravelPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.VelocityPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.ImuPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.PitchRollPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.SpeedPlot.PropertyChanged += OnPlotPreferenceChanged;
        PreferencesPage.ElevationPlot.PropertyChanged += OnPlotPreferenceChanged;
    }

    private void OnPlotPreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(PlotPreferenceItemViewModel.Selected) or nameof(PlotPreferenceItemViewModel.SelectedSmoothing)))
        {
            return;
        }

        graphWorkspace.ApplyPlotPreferences(PreferencesPage.CreatePlotPreferences());
    }

    private void ApplyPlotAvailability(LiveSessionHeader? sessionHeader)
    {
        var travelAvailable = sessionHeader is { AcceptedTravelHz: > 0 };
        var imuAvailable = sessionHeader is { AcceptedImuHz: > 0 } &&
                           sessionHeader.GetActiveImuLocations().Count > 0;
        var pitchRollAvailable = HasLiveFramePitchRollSource(sessionHeader);
        var gpsAvailable = sessionHeader is { AcceptedGpsFixHz: > 0 };

        PreferencesPage.ApplyPlotAvailability(
            travelAvailable,
            travelAvailable,
            imuAvailable,
            pitchRollAvailable,
            gpsAvailable,
            gpsAvailable);
    }

    private static bool HasLiveFramePitchRollSource(LiveSessionHeader? sessionHeader)
    {
        if (sessionHeader is not { AcceptedImuHz: > 0 })
        {
            return false;
        }

        return sessionHeader.GetActiveImuLocations().Contains(LiveImuLocation.Frame) &&
            sessionHeader.ImuCalibrationScales.GetAccelScale(LiveImuLocation.Frame) > 0 &&
            sessionHeader.ImuCalibrationScales.GetGyroScale(LiveImuLocation.Frame) > 0;
    }

    private SessionPreferences CreateCurrentSessionPreferences()
    {
        return new SessionPreferences(
            PreferencesPage.CreatePlotPreferences(),
            new SessionStatisticsPreferences(
                SelectedTravelHistogramMode,
                SelectedVelocityAverageMode,
                SelectedBalanceDisplacementMode,
                SelectedBalanceSpeedMode,
                SelectedSessionAnalysisTargetProfile),
            PreferencesPage.CreateProcessingPreferences(),
            graphWorkspace.GraphPreferences);
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
            ImuMaximum: 5,
            PitchRollMaximum: 15);
    }

    private bool IsTravelStatisticsExpected(bool sideConfigured)
    {
        return sideConfigured && ControlState.SessionHeader is { AcceptedTravelHz: > 0 };
    }

    private bool IsBalanceStatisticsExpected()
    {
        return hasFrontTravelCalibration &&
            hasRearTravelCalibration &&
            ControlState.SessionHeader is { AcceptedTravelHz: > 0 };
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
            graphWorkspace.ApplyGraphDataPresence(
                presence.HasTravelData,
                presence.HasImuData,
                presence.HasPitchRollData);
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
        ApplyPlotAvailability(snapshot.Controls.SessionHeader);
        mediaWorkspace.ApplySessionHeader(snapshot.Controls.SessionHeader);
        TelemetryData = snapshot.StatisticsTelemetry;
        DamperPercentages = snapshot.DamperPercentages;
        var trackTimelineContext = CreateLiveTrackTimelineContext(snapshot.Controls);
        graphWorkspace.ApplyTrackPresentation(snapshot.SessionTrackPoints, trackTimelineContext);
        mediaWorkspace.SetTrackPoints(snapshot.SessionTrackPoints, trackTimelineContext);

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

    private static TrackTimeRange? CreateLiveTrackTimelineContext(LiveSessionControlState controls)
    {
        var durationSeconds = controls.CaptureDuration.TotalSeconds;
        return controls.CaptureStartUtc is { } captureStartUtc
               && double.IsFinite(durationSeconds)
               && durationSeconds > 0
            ? new TrackTimeRange(captureStartUtc.ToUnixTimeSeconds(), durationSeconds)
            : null;
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

        DamperPage.ApplyDamperPercentages(data.DamperPercentages);
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
        DamperPage.ClearDamperPercentages();

        BalancePage.CompressionBalance = null;
        BalancePage.ReboundBalance = null;
        BalancePage.CompressionBalanceState = SurfacePresentationState.Hidden;
        BalancePage.ReboundBalanceState = SurfacePresentationState.Hidden;

        DamperPercentages = SessionDamperPercentages.Empty;
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

    private readonly record struct GraphBatchPresence(bool HasTravelData, bool HasImuData, bool HasPitchRollData)
    {
        public bool HasAnyData => HasTravelData || HasImuData || HasPitchRollData;

        public static GraphBatchPresence FromBatch(LiveGraphBatch batch)
        {
            return new GraphBatchPresence(
                HasTravelData: batch.TravelTimes.Count > 0
                    || batch.FrontTravel.Count > 0
                    || batch.RearTravel.Count > 0
                    || batch.VelocityTimes.Count > 0
                    || batch.FrontVelocity.Count > 0
                    || batch.RearVelocity.Count > 0,
                HasImuData: HasAnyImuData(batch),
                HasPitchRollData: HasAnyPitchRollData(batch));
        }

        public GraphBatchPresence Combine(GraphBatchPresence other) => new(
            HasTravelData || other.HasTravelData,
            HasImuData || other.HasImuData,
            HasPitchRollData || other.HasPitchRollData);

        private static bool HasAnyPitchRollData(LiveGraphBatch batch)
        {
            return batch.FramePitchRollTimes.Count > 0 ||
                batch.FramePitchDegrees.Count > 0 ||
                batch.FrameRollDegrees.Count > 0;
        }

        private static bool HasAnyImuData(LiveGraphBatch batch)
        {
            foreach (var series in batch.ImuTimes.Values)
            {
                if (series.Count > 0)
                {
                    return true;
                }
            }

            foreach (var series in batch.ImuVibrationRms.Values)
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

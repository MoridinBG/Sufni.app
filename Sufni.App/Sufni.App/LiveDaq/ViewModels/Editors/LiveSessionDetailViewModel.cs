using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Queries;
#if SUFNI_PROFILING_DIAGNOSTICS
using Sufni.App.LiveDaq.Services;
using Sufni.Profiling;
#endif
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.SessionPages;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Insights.ViewModels.Editors;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Plots;
namespace Sufni.App.LiveDaq.ViewModels.Editors;

public sealed partial class LiveSessionDetailViewModel : TabPageViewModelBase,
    ISessionShellMobileWorkspace,
    ISessionAnalysisWorkspace,
    ISessionSidebarWorkspace,
    ILiveSessionControlsWorkspace
{
    private readonly LiveSessionSignalsWorkspaceViewModel signalsWorkspace;
    private readonly LiveSessionMediaWorkspaceViewModel mediaWorkspace;
    private readonly ILiveSessionService liveSessionService;
    private readonly ISessionCoordinator sessionCoordinator;
    private readonly IBikeCoordinator? bikeCoordinator;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private IDisposable? uiRefreshTimer;
    private readonly System.Threading.Lock presentationGate = new();
    private readonly System.Threading.Lock signalBatchRefreshGate = new();
    private bool hasLoaded;
    private bool isViewLoaded;
    private long? blockedSavedCaptureRevision;
    private LiveSessionPresentationSnapshot pendingPresentation = LiveSessionPresentationSnapshot.Empty;
    private bool hasPendingPresentation;
    private SignalBatchPresence pendingSignalBatchPresence;
    private bool hasPendingSignalBatchRefresh;
    private readonly bool hasFrontTravelCalibration;
    private readonly bool hasRearTravelCalibration;
    private SessionPresentationDimensions? lastPresentationDimensions;
    private TelemetryData? lastBakedTelemetryData;
    private CancellationTokenSource? bakeCts;
    private DampingSpeedCutoffOwner? dampingSpeedCutoffOwner;
    private DampingSpeedCutoffs persistedDampingSpeedCutoffs = DampingSpeedCutoffs.Default;
    private DampingSpeedCutoffs? dampingSpeedCutoffPreviewOrigin;
    private int selectedPageIndex;

    public string IdentityKey { get; }

    public TabPageViewModelBase Editor => this;
    public Guid SetupId { get; }
    public string? SetupName { get; }
    public Guid BikeId { get; }
    public string? BikeName { get; }
    public bool CanEditDampingSpeedCutoffs => dampingSpeedCutoffOwner is not null;
    public IRelayCommand<TelemetryRangeSelection?> SelectAnalysisRangeCommand { get; } =
        new RelayCommand<TelemetryRangeSelection?>(_ => { });
    public TelemetryRangeSelection? ActiveFrontAnalysisSelection => null;
    public TelemetryRangeSelection? ActiveRearAnalysisSelection => null;

    public ILiveSessionSignalsWorkspace SignalsWorkspace => signalsWorkspace;
    public ISessionMediaWorkspace MediaWorkspace => mediaWorkspace;
    public NotesPageViewModel NotesPage { get; } = new();
    public PreferencesPageViewModel PreferencesPage { get; } = new();
    public SpringPageViewModel SpringPage { get; }
    public DampingPageViewModel DampingPage { get; }
    public BalancePageViewModel BalancePage { get; }
    public LiveSignalsPageViewModel LiveSignalsPage { get; }
    public ObservableCollection<PageViewModelBase> Pages { get; }
    public int SelectedPageIndex
    {
        get => selectedPageIndex;
        set => SetSelectedPageIndex(value);
    }
    public PageViewModelBase? SelectedPage => Pages.Count == 0 ? null : Pages[SelectedPageIndex];
    public int PageCount => Pages.Count;
    public string SelectedPageDisplayName => SelectedPage?.DisplayName ?? string.Empty;
    public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();

    public SuspensionSettings ForkSettings => NotesPage.ForkSettings;
    public SuspensionSettings ShockSettings => NotesPage.ShockSettings;

    public string? DescriptionText
    {
        get => NotesPage.Description;
        set => NotesPage.Description = value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrontAnalysisState))]
    [NotifyPropertyChangedFor(nameof(RearAnalysisState))]
    [NotifyPropertyChangedFor(nameof(CompressionBalanceState))]
    [NotifyPropertyChangedFor(nameof(ReboundBalanceState))]
    public partial TelemetryData? TelemetryData { get; set; }

    [ObservableProperty]
    public partial SessionDampingPercentages DampingPercentages { get; set; } = SessionDampingPercentages.Empty;

    // Kept in field form: the constructor writes the backing field directly to seed the
    // initial value WITHOUT firing OnDampingSpeedCutoffsChanged, which recomputes damping
    // percentages against state not yet initialized at construction time (NRE otherwise).
    // All runtime writes still go through the generated DampingSpeedCutoffs property.
    [ObservableProperty]
    private DampingSpeedCutoffs dampingSpeedCutoffs = DampingSpeedCutoffs.Default;

    [ObservableProperty]
    private DampingSpeedCutoffs plotDampingSpeedCutoffs = DampingSpeedCutoffs.Default;

    public TravelDistributionMode SelectedTravelDistributionMode
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
            }
        }
    } = TravelDistributionMode.ActiveSuspension;

    public BalanceDisplacementMode SelectedBalanceDisplacementMode
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
            }
        }
    } = BalanceDisplacementMode.Zenith;

    public BalanceSpeedMode SelectedBalanceSpeedMode
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
            }
        }
    } = BalanceSpeedMode.Both;

    public VelocityAverageMode SelectedVelocityAverageMode
    {
        get => field;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(SessionAnalysisModesText));
                RecomputeDampingPercentagesForSelectedVelocityAverageMode();
            }
        }
    } = VelocityAverageMode.SampleAveraged;

    public SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile
    {
        get => field;
        set => SetProperty(ref field, value);
    } = SessionInsightsTargetProfile.Trail;

    public IReadOnlyList<TravelDistributionModeOption> TravelDistributionModeOptions { get; } = SessionInsightsPresentation.TravelDistributionModeOptions;
    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } = SessionInsightsPresentation.BalanceDisplacementModeOptions;
    public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } = SessionInsightsPresentation.BalanceSpeedModeOptions;
    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } = SessionInsightsPresentation.VelocityAverageModeOptions;
    public IReadOnlyList<SessionInsightsTargetProfileOption> SessionInsightsTargetProfileOptions { get; } = SessionInsightsPresentation.SessionInsightsTargetProfileOptions;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FrontAnalysisState))]
    [NotifyPropertyChangedFor(nameof(RearAnalysisState))]
    [NotifyPropertyChangedFor(nameof(CompressionBalanceState))]
    [NotifyPropertyChangedFor(nameof(ReboundBalanceState))]
    public partial LiveSessionControlState ControlState { get; set; } = LiveSessionControlState.Empty;

    [ObservableProperty]
    public partial SessionScreenPresentationState ScreenState { get; set; } = SessionScreenPresentationState.Ready;

    public SessionOperationPresentationState SessionOperationState => SessionOperationPresentationState.Hidden;

    public SurfacePresentationState FrontAnalysisState => AnalysisSurfaceState.ForSuspension(IsTravelAnalysisExpected(hasFrontTravelCalibration), TelemetryData, SuspensionType.Front);
    public SurfacePresentationState RearAnalysisState => AnalysisSurfaceState.ForSuspension(IsTravelAnalysisExpected(hasRearTravelCalibration), TelemetryData, SuspensionType.Rear);
    public SurfacePresentationState CompressionBalanceState => AnalysisSurfaceState.ForBalance(IsBalanceAnalysisExpected(), TelemetryData, BalanceType.Compression);
    public SurfacePresentationState ReboundBalanceState => AnalysisSurfaceState.ForBalance(IsBalanceAnalysisExpected(), TelemetryData, BalanceType.Rebound);
    public SurfacePresentationState FrontForkVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState FrontFrameVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState RearForkVibrationState => SurfacePresentationState.Hidden;
    public SurfacePresentationState RearFrameVibrationState => SurfacePresentationState.Hidden;
    public TelemetryTimeRange? AnalysisRange => null;
    public SessionInsightsResult SessionInsights => SessionInsightsResult.Hidden;
    public string SessionAnalysisRangeText => "Live session";
    public string SessionAnalysisModesText => SessionInsightsPresentation.DescribeModes(
        SelectedTravelDistributionMode,
        SelectedVelocityAverageMode,
        SelectedBalanceDisplacementMode,
        SelectedBalanceSpeedMode);

    public LiveSessionDetailViewModel(
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService,
        ISessionCoordinator sessionCoordinator,
        ISessionPresentationService sessionPresentationService,
        IBackgroundTaskRunner backgroundTaskRunner,
        IMapViewModelFactory mapViewModelFactory,
        IShellCoordinator shell,
        IDialogService dialogService,
        IUiThreadDispatcher uiThreadDispatcher,
        IBikeCoordinator? bikeCoordinator = null)
        : base(shell, dialogService, uiThreadDispatcher)
    {
        IdentityKey = context.IdentityKey;
        SetupId = context.SetupId;
        SetupName = context.SetupName;
        BikeId = context.BikeId;
        BikeName = context.BikeName;
        dampingSpeedCutoffOwner = context.DampingSpeedCutoffOwner;
        persistedDampingSpeedCutoffs = context.DampingSpeedCutoffs.ClampValues();
        dampingSpeedCutoffs = persistedDampingSpeedCutoffs;
        plotDampingSpeedCutoffs = persistedDampingSpeedCutoffs;
        this.liveSessionService = liveSessionService;
        this.sessionCoordinator = sessionCoordinator;
        this.bikeCoordinator = bikeCoordinator;
        this.sessionPresentationService = sessionPresentationService;
        this.backgroundTaskRunner = backgroundTaskRunner;
        hasFrontTravelCalibration = context.TravelCalibration.Front is not null;
        hasRearTravelCalibration = context.TravelCalibration.Rear is not null;

        var timeline = new SessionTimelineLinkViewModel();
        signalsWorkspace = new LiveSessionSignalsWorkspaceViewModel(timeline, CreatePlotRanges(context), liveSessionService.SignalBatches);
        mediaWorkspace = new LiveSessionMediaWorkspaceViewModel(mapViewModelFactory, timeline);
        Name = CreateDefaultName(DateTimeOffset.Now);
        LiveSignalsPage = new LiveSignalsPageViewModel(signalsWorkspace, mediaWorkspace);
        SpringPage = new SpringPageViewModel(this);
        DampingPage = new DampingPageViewModel(this);
        BalancePage = new BalancePageViewModel(this);
        Pages = [LiveSignalsPage, SpringPage, DampingPage, NotesPage, PreferencesPage];
        Pages.CollectionChanged += OnPagesChanged;
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

        isViewLoaded = true;
        if (hasLoaded)
        {
            if (IsTabActive)
            {
                StartForegroundUpdates();
            }

            return;
        }

        hasLoaded = true;
        await mediaWorkspace.InitializeAsync();
        await liveSessionService.EnsureAttachedAsync();

        if (IsTabActive)
        {
            StartForegroundUpdates();
        }
    }

    [RelayCommand]
    private async Task Unloaded()
    {
        isViewLoaded = false;
        StopForegroundUpdates();
        await Task.CompletedTask;
    }

    protected override void OnActivated()
    {
        if (isViewLoaded)
        {
            StartForegroundUpdates();
        }
    }

    protected override void OnDeactivated()
    {
        if (isViewLoaded)
        {
            StopForegroundUpdates();
        }
    }

    private void StartForegroundUpdates()
    {
        // Live session updates arrive far faster than the controls need to repaint.
        // Keep the latest snapshot and project it into the UI at a fixed cadence.
        uiRefreshTimer ??= PeriodicUiTimer.SchedulePeriodic(
            TimeSpan.FromMilliseconds(PlotSettings.LiveUiRefreshIntervalMs),
            RefreshUi);

        EnsureScopedSubscription(disposables =>
        {
            disposables.Add(liveSessionService.Snapshots.Subscribe(QueuePresentationRefresh));
            disposables.Add(liveSessionService.SignalBatches.Subscribe(QueueSignalBatchRefresh));
        });

        ApplyPresentation(liveSessionService.Current);
        RefreshUi();
    }

    private void StopForegroundUpdates()
    {
        uiRefreshTimer?.Dispose();
        uiRefreshTimer = null;
        CancelBake();
        lastBakedTelemetryData = null;
        DisposeScopedSubscriptions();
        ClearPendingForegroundUpdates();
    }

    private void ClearPendingForegroundUpdates()
    {
        lock (presentationGate)
        {
            hasPendingPresentation = false;
        }

        lock (signalBatchRefreshGate)
        {
            pendingSignalBatchPresence = default;
            hasPendingSignalBatchRefresh = false;
        }
    }

    private static SessionPresentationDimensions? CreatePresentationDimensions(Rect? bounds)
    {
        if (bounds is not Rect rect || rect.Width <= 0 || rect.Height <= 0)
        {
            return null;
        }

        return new SessionPresentationDimensions((int)rect.Width, (int)(rect.Height / 2.0));
    }

    private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        var clampedIndex = ClampSelectedPageIndex(selectedPageIndex);
        SetProperty(ref selectedPageIndex, clampedIndex, nameof(SelectedPageIndex));
        NotifySelectedPagePropertiesChanged();
    }

    private void SetSelectedPageIndex(int value)
    {
        var clampedIndex = ClampSelectedPageIndex(value);
        if (SetProperty(ref selectedPageIndex, clampedIndex, nameof(SelectedPageIndex)))
        {
            NotifySelectedPagePropertiesChanged();
        }
    }

    private int ClampSelectedPageIndex(int value)
    {
        if (Pages.Count == 0)
        {
            return 0;
        }

        if (value < 0)
        {
            return 0;
        }

        if (value >= Pages.Count)
        {
            return Pages.Count - 1;
        }

        return value;
    }

    private void NotifySelectedPagePropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(SelectedPageDisplayName));
    }

    protected override async Task CloseImplementation()
    {
        isViewLoaded = false;
        StopForegroundUpdates();
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
#if SUFNI_PROFILING_DIAGNOSTICS
        var profilingCorrelationId = ProfilingBench01.BeginSave();
        var profilingSaved = false;
        using var profilingStage = ProfilingRuntime.BeginStage(
            ProfilingBench01.Scenario,
            "LiveSave.UICommand",
            profilingCorrelationId);
#endif

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
#if SUFNI_PROFILING_DIAGNOSTICS
            ProfilingBench01.SessionCreated(session.Id, profilingCorrelationId);
#endif

            var result = await sessionCoordinator.SaveLiveCaptureAsync(session, capture, CreateCurrentSessionPreferences());
            switch (result)
            {
                case LiveSessionSaveResult.Saved saved:
#if SUFNI_PROFILING_DIAGNOSTICS
                    profilingSaved = true;
#endif
                    blockedSavedCaptureRevision = savedCaptureRevision;
                    if (saved.PublicationWarning is not null)
                    {
                        ErrorMessages.Add(saved.PublicationWarning);
                    }

                    try
                    {
                        await liveSessionService.ResetCaptureAsync();
                        ResetCapturePresentation();
                        if (shouldRefreshAutoName)
                        {
                            Name = CreateDefaultName(DateTimeOffset.Now);
                        }

                        await sessionCoordinator.OpenEditAsync(saved.SessionId);
#if SUFNI_PROFILING_DIAGNOSTICS
                        profilingStage?.SetResult(0, 0, "saved");
#endif
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
#if SUFNI_PROFILING_DIAGNOSTICS
                        profilingStage?.SetResult(0, 0, "saved_cleanup_failed");
#endif
                        ErrorMessages.Add($"Live session was saved, but post-save cleanup failed: {e.Message}");
                    }
                    break;

                case LiveSessionSaveResult.Failed failed:
#if SUFNI_PROFILING_DIAGNOSTICS
                    profilingStage?.SetResult(0, 0, "save_failed");
#endif
                    ErrorMessages.Add($"Live session could not be saved: {failed.ErrorMessage}");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
#if SUFNI_PROFILING_DIAGNOSTICS
            profilingStage?.SetResult(0, 0, profilingSaved ? "saved_cleanup_canceled" : "canceled");
            if (!profilingSaved)
            {
                ProfilingBench01.SaveFailed(profilingCorrelationId, "canceled");
            }
#endif
        }
        catch (Exception e)
        {
#if SUFNI_PROFILING_DIAGNOSTICS
            profilingStage?.SetResult(0, 0, "capture_failed");
            ProfilingBench01.SaveFailed(profilingCorrelationId, "capture_failed", e.Message);
#endif
            ErrorMessages.Add($"Live session could not be saved: {e.Message}");
        }
        finally
        {
            UiThreadDispatcher.Post(() =>
            {
                EvaluateDirtiness();
                RefreshCommandState();
            });
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
        PreferencesPage.TravelSignal.PropertyChanged += OnSignalPreferenceChanged;
        PreferencesPage.VelocitySignal.PropertyChanged += OnSignalPreferenceChanged;
        PreferencesPage.ImuSignal.PropertyChanged += OnSignalPreferenceChanged;
        PreferencesPage.PitchRollSignal.PropertyChanged += OnSignalPreferenceChanged;
        PreferencesPage.SpeedSignal.PropertyChanged += OnSignalPreferenceChanged;
        PreferencesPage.ElevationSignal.PropertyChanged += OnSignalPreferenceChanged;
    }

    private void OnSignalPreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not nameof(SignalPreferenceItemViewModel.SelectedSmoothing))
        {
            return;
        }

        signalsWorkspace.ApplySignalDisplayPreferences(PreferencesPage.CreateSignalDisplayPreferences());
    }

    private void ApplySignalAvailability(LiveSessionHeader? sessionHeader)
    {
        var travelAvailable = sessionHeader is { AcceptedTravelHz: > 0 };
        var imuAvailable = sessionHeader is { AcceptedImuHz: > 0 } &&
                           sessionHeader.GetActiveImuLocations().Count > 0;
        var pitchRollAvailable = HasLiveFramePitchRollSource(sessionHeader);
        var gpsAvailable = sessionHeader is { AcceptedGpsFixHz: > 0 };

        PreferencesPage.ApplySignalAvailability(
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
            PreferencesPage.CreateSignalDisplayPreferences(),
            new AnalysisPreferences(
                SelectedTravelDistributionMode,
                SelectedVelocityAverageMode,
                SelectedBalanceDisplacementMode,
                SelectedBalanceSpeedMode,
                SelectedSessionInsightsTargetProfile),
            PreferencesPage.CreateProcessingPreferences(),
            signalsWorkspace.SignalLayoutPreferences);
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

    private bool IsTravelAnalysisExpected(bool sideConfigured)
    {
        return sideConfigured && ControlState.SessionHeader is { AcceptedTravelHz: > 0 };
    }

    private bool IsBalanceAnalysisExpected()
    {
        return hasFrontTravelCalibration &&
            hasRearTravelCalibration &&
            ControlState.SessionHeader is { AcceptedTravelHz: > 0 };
    }

    private void QueueSignalBatchRefresh(LiveSignalBatch batch)
    {
        var presence = SignalBatchPresence.FromBatch(batch);
        if (!presence.HasAnyData)
        {
            return;
        }

        var shouldPost = false;
        lock (signalBatchRefreshGate)
        {
            pendingSignalBatchPresence = pendingSignalBatchPresence.Combine(presence);
            if (!hasPendingSignalBatchRefresh)
            {
                hasPendingSignalBatchRefresh = true;
                shouldPost = true;
            }
        }

        if (shouldPost)
        {
            UiThreadDispatcher.Post(FlushSignalBatchRefresh);
        }
    }

    private void FlushSignalBatchRefresh()
    {
        SignalBatchPresence presence;
        lock (signalBatchRefreshGate)
        {
            presence = pendingSignalBatchPresence;
            pendingSignalBatchPresence = default;
            hasPendingSignalBatchRefresh = false;
        }

        if (presence.HasAnyData && isViewLoaded && IsTabActive)
        {
            signalsWorkspace.ApplySignalDataPresence(
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

        signalsWorkspace.ApplySessionHeader(snapshot.Controls.SessionHeader);
        ApplySignalAvailability(snapshot.Controls.SessionHeader);
        mediaWorkspace.ApplySessionHeader(snapshot.Controls.SessionHeader);
        TelemetryData = snapshot.AnalysisTelemetry;
        ApplyModeAwareDampingPercentages(snapshot.DampingPercentages);
        var trackTimelineContext = CreateLiveTrackTimelineContext(snapshot.Controls);
        signalsWorkspace.ApplyTrackPresentation(snapshot.SessionTrackPoints, trackTimelineContext);
        mediaWorkspace.SetTrackPoints(snapshot.SessionTrackPoints, trackTimelineContext);

        if (snapshot.Controls.SessionHeader is { } header)
        {
            Timestamp = header.SessionStartUtc.LocalDateTime;
        }

        ControlState = ApplyControlFlags(snapshot.Controls);
        EvaluateDirtiness();
        RefreshCommandState();

        MaybeQueueBake(snapshot.AnalysisTelemetry);
    }

    private void MaybeQueueBake(TelemetryData? telemetryData)
    {
        if (!isViewLoaded ||
            !IsTabActive ||
            telemetryData is null ||
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
        var cutoffs = DampingSpeedCutoffs;

        _ = BakeCachePresentationAsync(service, telemetryData, dimensions, cts, cutoffs);
    }

    private async Task BakeCachePresentationAsync(
        ISessionPresentationService service,
        TelemetryData telemetryData,
        SessionPresentationDimensions dimensions,
        CancellationTokenSource cts,
        DampingSpeedCutoffs cutoffs)
    {
        try
        {
            var data = await backgroundTaskRunner.RunAsync(
                () => service.BuildCachePresentation(telemetryData, dimensions, cts.Token, cutoffs),
                cts.Token);

            if (cts.IsCancellationRequested)
            {
                return;
            }

            UiThreadDispatcher.Post(() =>
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                ApplyCachePresentation(data);
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            UiThreadDispatcher.Post(() => ErrorMessages.Add($"Live analysis render failed: {e.Message}"));
        }
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
        var hasFrontTravelDistribution = !string.IsNullOrWhiteSpace(data.FrontTravelDistribution);
        var hasRearTravelDistribution = !string.IsNullOrWhiteSpace(data.RearTravelDistribution);
        var hasFrontVelocityDistribution = !string.IsNullOrWhiteSpace(data.FrontVelocityDistribution);
        var hasRearVelocityDistribution = !string.IsNullOrWhiteSpace(data.RearVelocityDistribution);
        var hasCompressionBalance = !string.IsNullOrWhiteSpace(data.CompressionBalance);
        var hasReboundBalance = !string.IsNullOrWhiteSpace(data.ReboundBalance);

        var frontAnalysis = FrontAnalysisState;
        var rearAnalysis = RearAnalysisState;
        var compressionBalance = CompressionBalanceState;
        var reboundBalance = ReboundBalanceState;

        SpringPage.FrontTravelDistribution = data.FrontTravelDistribution;
        SpringPage.RearTravelDistribution = data.RearTravelDistribution;
        SpringPage.FrontDistributionState = ResolveSurfaceState(hasFrontTravelDistribution, frontAnalysis);
        SpringPage.RearDistributionState = ResolveSurfaceState(hasRearTravelDistribution, rearAnalysis);

        DampingPage.FrontVelocityDistribution = data.FrontVelocityDistribution;
        DampingPage.RearVelocityDistribution = data.RearVelocityDistribution;
        DampingPage.FrontDistributionState = ResolveSurfaceState(hasFrontVelocityDistribution, frontAnalysis);
        DampingPage.RearDistributionState = ResolveSurfaceState(hasRearVelocityDistribution, rearAnalysis);

        ApplyModeAwareDampingPercentages(data.DampingPercentages);

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

    private void ApplyDampingPercentages(SessionDampingPercentages percentages)
    {
        DampingPercentages = percentages;
        DampingPage.ApplyDampingPercentages(percentages);
    }

    public void PreviewDampingSpeedCutoff(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond)
    {
        if (dampingSpeedCutoffOwner is null)
        {
            return;
        }

        dampingSpeedCutoffPreviewOrigin ??= DampingSpeedCutoffs;
        DampingSpeedCutoffs = DampingSpeedCutoffs.With(
            side,
            circuit,
            DampingCutoffEditing.RoundDragValue(cutoffMmPerSecond));
    }

    public void CancelDampingSpeedCutoffPreview()
    {
        if (dampingSpeedCutoffPreviewOrigin is not { } origin)
        {
            return;
        }

        dampingSpeedCutoffPreviewOrigin = null;
        DampingSpeedCutoffs = origin;
    }

    public async Task CommitDampingSpeedCutoffAsync(
        SuspensionType side,
        DampingSpeedCircuit circuit,
        double cutoffMmPerSecond)
    {
        if (dampingSpeedCutoffOwner is not { } owner || bikeCoordinator is null)
        {
            return;
        }

        dampingSpeedCutoffPreviewOrigin = null;
        var committedCutoffs = DampingSpeedCutoffs.With(
            side,
            circuit,
            DampingCutoffEditing.RoundDragValue(cutoffMmPerSecond));
        DampingSpeedCutoffs = committedCutoffs;
        PlotDampingSpeedCutoffs = committedCutoffs;

        var result = await bikeCoordinator.UpdateDampingSpeedCutoffAsync(
            owner.BikeId,
            owner.BaselineUpdated,
            side,
            circuit,
            committedCutoffs.Get(side, circuit));

        switch (result)
        {
            case BikeDampingSpeedCutoffUpdateResult.Saved saved:
                ApplyPersistedDampingSpeedCutoffs(saved.Snapshot.DampingSpeedCutoffs, saved.Snapshot.Updated);
                break;

            case BikeDampingSpeedCutoffUpdateResult.Conflict conflict:
                ApplyPersistedDampingSpeedCutoffs(conflict.CurrentSnapshot.DampingSpeedCutoffs, conflict.CurrentSnapshot.Updated);
                ErrorMessages.Add("Bike damping cutoff changed elsewhere. Reloaded the latest cutoff.");
                break;

            case BikeDampingSpeedCutoffUpdateResult.Failed failed:
                DampingSpeedCutoffs = persistedDampingSpeedCutoffs;
                PlotDampingSpeedCutoffs = persistedDampingSpeedCutoffs;
                ErrorMessages.Add($"Could not save damping cutoff: {failed.ErrorMessage}");
                break;
        }
    }

    private void ApplyPersistedDampingSpeedCutoffs(DampingSpeedCutoffs cutoffs, long baselineUpdated)
    {
        persistedDampingSpeedCutoffs = cutoffs.ClampValues();
        dampingSpeedCutoffPreviewOrigin = null;
        dampingSpeedCutoffOwner = new DampingSpeedCutoffOwner(BikeId, baselineUpdated);
        OnPropertyChanged(nameof(CanEditDampingSpeedCutoffs));
        PlotDampingSpeedCutoffs = persistedDampingSpeedCutoffs;
        DampingSpeedCutoffs = persistedDampingSpeedCutoffs;
    }

    private void ApplyModeAwareDampingPercentages(SessionDampingPercentages sampleAveragedPercentages)
    {
        if (TelemetryData is null)
        {
            ApplyDampingPercentages(SessionDampingPercentages.Empty);
            return;
        }

        if (SelectedVelocityAverageMode == VelocityAverageMode.SampleAveraged)
        {
            ApplyDampingPercentages(sampleAveragedPercentages);
            return;
        }

        RecomputeDampingPercentagesForSelectedVelocityAverageMode();
    }

    private void RecomputeDampingPercentagesForSelectedVelocityAverageMode()
    {
        if (TelemetryData is null)
        {
            ApplyDampingPercentages(SessionDampingPercentages.Empty);
            return;
        }

        ApplyDampingPercentages(sessionPresentationService.CalculateDampingPercentages(
            TelemetryData,
            AnalysisRange,
            SelectedVelocityAverageMode,
            DampingSpeedCutoffs));
    }

    partial void OnDampingSpeedCutoffsChanged(DampingSpeedCutoffs value)
    {
        RecomputeDampingPercentagesForSelectedVelocityAverageMode();
    }

    partial void OnPlotDampingSpeedCutoffsChanged(DampingSpeedCutoffs value)
    {
        lastBakedTelemetryData = null;
        MaybeQueueBake(TelemetryData);
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
        ClearAnalysisPages();
        signalsWorkspace.Timeline.Reset();
        ApplyPresentation(liveSessionService.Current);
    }

    private void ClearAnalysisPages()
    {
        SpringPage.FrontTravelDistribution = null;
        SpringPage.RearTravelDistribution = null;
        SpringPage.FrontDistributionState = SurfacePresentationState.Hidden;
        SpringPage.RearDistributionState = SurfacePresentationState.Hidden;

        DampingPage.FrontVelocityDistribution = null;
        DampingPage.RearVelocityDistribution = null;
        DampingPage.FrontDistributionState = SurfacePresentationState.Hidden;
        DampingPage.RearDistributionState = SurfacePresentationState.Hidden;
        DampingPage.ClearDampingPercentages();

        BalancePage.CompressionBalance = null;
        BalancePage.ReboundBalance = null;
        BalancePage.CompressionBalanceState = SurfacePresentationState.Hidden;
        BalancePage.ReboundBalanceState = SurfacePresentationState.Hidden;

        DampingPercentages = SessionDampingPercentages.Empty;
        EnsureBalancePage(balanceAvailable: false);
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

    private readonly record struct SignalBatchPresence(bool HasTravelData, bool HasImuData, bool HasPitchRollData)
    {
        public bool HasAnyData => HasTravelData || HasImuData || HasPitchRollData;

        public static SignalBatchPresence FromBatch(LiveSignalBatch batch)
        {
            return new SignalBatchPresence(
                HasTravelData: batch.TravelTimes.Count > 0
                    || batch.FrontTravel.Count > 0
                    || batch.RearTravel.Count > 0
                    || batch.VelocityTimes.Count > 0
                    || batch.FrontVelocity.Count > 0
                    || batch.RearVelocity.Count > 0,
                HasImuData: HasAnyImuData(batch),
                HasPitchRollData: HasAnyPitchRollData(batch));
        }

        public SignalBatchPresence Combine(SignalBatchPresence other) => new(
            HasTravelData || other.HasTravelData,
            HasImuData || other.HasImuData,
            HasPitchRollData || other.HasPitchRollData);

        private static bool HasAnyPitchRollData(LiveSignalBatch batch)
        {
            return batch.FramePitchRollTimes.Count > 0 ||
                batch.FramePitchDegrees.Count > 0 ||
                batch.FrameRollDegrees.Count > 0;
        }

        private static bool HasAnyImuData(LiveSignalBatch batch)
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

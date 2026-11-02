using Sufni.App.Shared.Models;
using Sufni.App.Segments.ViewModels;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Bikes.Models;
using Sufni.App.MapsAndTracks.Models;
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
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.SessionPages;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.Sessions.Store;
using Serilog;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Analysis.Services;
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
    private readonly IUiTimerScheduler uiTimerScheduler;
    private readonly LiveSessionSignalsWorkspaceViewModel signalsWorkspace;
    private readonly LiveSessionMediaWorkspaceViewModel mediaWorkspace;
    private readonly ILiveSessionService liveSessionService;
    private readonly ILiveDaqCoordinator liveDaqCoordinator;
    private readonly IBikeCoordinator? bikeCoordinator;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly LiveSessionAnalysisResultSource analysisResults = new();
    public ISessionAnalysisResultSource AnalysisResultState => analysisResults;
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
    private readonly List<Task> bakeTasks = [];
    private LiveAnalysisFailure? shownAnalysisFailure;
    private FailurePresentation? shownConnectionFailure;
    private DampingSpeedCutoffs? persistedDampingSpeedCutoffs;
    private DampingSpeedCutoffs? dampingSpeedCutoffPreviewOrigin;
    private int selectedPageIndex;

    public string IdentityKey { get; }

    public TabPageViewModelBase Editor => this;
    public Guid SetupId { get; }
    public string? SetupName { get; }
    public Guid BikeId { get; }
    public string? BikeName { get; }
    public bool CanEditDampingSpeedCutoffs => persistedDampingSpeedCutoffs is not null;
    public IRelayCommand<TelemetryRangeSelection?> SelectAnalysisRangeCommand { get; } =
        new RelayCommand<TelemetryRangeSelection?>(_ => { });
    public TelemetryRangeSelection? ActiveFrontAnalysisSelection => null;
    public TelemetryRangeSelection? ActiveRearAnalysisSelection => null;

    public ILiveSessionSignalsWorkspace SignalsWorkspace => signalsWorkspace;
    public ISessionMediaWorkspace MediaWorkspace => mediaWorkspace;
    public NotesPageViewModel NotesPage { get; } = new();
    public PreferencesPageViewModel PreferencesPage { get; }
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
    public SegmentComparisonViewModel? SegmentComparison => null;

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
                UpdateAnalysisOptions();
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
                UpdateAnalysisOptions();
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
                UpdateAnalysisOptions();
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
                UpdateAnalysisOptions();
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

    public SurfacePresentationState FrontAnalysisState => analysisResults.Current?.FrontState ?? AnalysisSurfaceState.ForSuspension(IsTravelAnalysisExpected(hasFrontTravelCalibration), null, SuspensionType.Front);
    public SurfacePresentationState RearAnalysisState => analysisResults.Current?.RearState ?? AnalysisSurfaceState.ForSuspension(IsTravelAnalysisExpected(hasRearTravelCalibration), null, SuspensionType.Rear);
    public SurfacePresentationState CompressionBalanceState => analysisResults.Current?.CompressionState ?? AnalysisSurfaceState.ForBalance(IsBalanceAnalysisExpected(), null, BalanceType.Compression);
    public SurfacePresentationState ReboundBalanceState => analysisResults.Current?.ReboundState ?? AnalysisSurfaceState.ForBalance(IsBalanceAnalysisExpected(), null, BalanceType.Rebound);
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
        ILiveDaqCoordinator liveDaqCoordinator,
        ISessionPresentationService sessionPresentationService,
        IBackgroundTaskRunner backgroundTaskRunner,
        IMapViewModelFactory mapViewModelFactory,
        IShellCoordinator shell,
        IDialogService dialogService,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider,
        IUiTimerScheduler uiTimerScheduler,
        IBikeCoordinator? bikeCoordinator = null)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.uiTimerScheduler = uiTimerScheduler;
        IdentityKey = context.IdentityKey;
        SetupId = context.SetupId;
        SetupName = context.SetupName;
        BikeId = context.BikeId;
        BikeName = context.BikeName;
        persistedDampingSpeedCutoffs = context.DampingSpeedCutoffs.ClampValues();
        dampingSpeedCutoffs = persistedDampingSpeedCutoffs;
        plotDampingSpeedCutoffs = persistedDampingSpeedCutoffs;
        this.liveSessionService = liveSessionService;
        this.liveDaqCoordinator = liveDaqCoordinator;
        this.bikeCoordinator = bikeCoordinator;
        this.sessionPresentationService = sessionPresentationService;
        this.backgroundTaskRunner = backgroundTaskRunner;
        hasFrontTravelCalibration = context.TravelCalibration.Front is not null;
        hasRearTravelCalibration = context.TravelCalibration.Rear is not null;

        var timeline = new SessionTimelineLinkViewModel();
        signalsWorkspace = new LiveSessionSignalsWorkspaceViewModel(timeline, CreatePlotRanges(context), liveSessionService.SignalBatches);
        PreferencesPage = new PreferencesPageViewModel(() => signalsWorkspace.SignalDisplayPreferences, ApplyRuntimeSignalPreferences);
        mediaWorkspace = new LiveSessionMediaWorkspaceViewModel(mapViewModelFactory, timeline);
        Name = CreateDefaultName(Clock.GetLocalNow());
        LiveSignalsPage = new LiveSignalsPageViewModel(signalsWorkspace, mediaWorkspace);
        SpringPage = new SpringPageViewModel(this);
        DampingPage = new DampingPageViewModel(this);
        BalancePage = new BalancePageViewModel(this);
        Pages = [LiveSignalsPage, SpringPage, DampingPage, NotesPage, PreferencesPage];
        Pages.CollectionChanged += OnPagesChanged;
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
        var start = await liveSessionService.EnsureAttachedAsync();
        if (start is LivePreviewStartResult.Busy busy) Notifications.Add(LiveDaqMessages.Busy(busy.Holder));
        if (start is LivePreviewStartResult.Unavailable unavailable) Notifications.Add(unavailable.Message);

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
        uiRefreshTimer ??= uiTimerScheduler.SchedulePeriodic(
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
        await Task.WhenAll(bakeTasks);
        analysisResults.Dispose();
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
            var saveTime = Clock.GetLocalNow();
            var sessionName = shouldRefreshAutoName ? CreateDefaultName(saveTime) : Name!;
            var session = new Session(
                id: Guid.NewGuid(),
                name: sessionName,
                description: DescriptionText ?? string.Empty,
                setup: SetupId)
            {
                VelocityFilterWindowMilliseconds = PreferencesPage.CreateProcessingOptions().ClampedVelocityFilterWindowMilliseconds,
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

            var result = await liveDaqCoordinator.SaveCaptureAsync(SessionSnapshot.From(session), liveSessionService, CreateCurrentSessionPreferences());
            switch (result)
            {
                case LiveSessionSaveResult.Saved saved:
                    blockedSavedCaptureRevision = saved.CaptureRevision;
                    if (saved.PreferenceWarning is not null)
                    {
                        ErrorMessages.Add(FailurePresentation.From(LiveSessionSaveFailureReason.Preferences, saved.PreferenceWarning));
                    }
                    if (saved.PublicationWarning is not null)
                    {
                        ErrorMessages.Add(FailurePresentation.From(LiveSessionSaveFailureReason.Publish, saved.PublicationWarning));
                    }

                    if (saved.CleanupFailure is not null)
                    {
                        ErrorMessages.Add(FailurePresentation.From(LiveSessionSaveFailureReason.Cleanup, saved.CleanupFailure));
                        break;
                    }
                    try
                    {
                        ResetCapturePresentation();
                        if (shouldRefreshAutoName)
                        {
                            Name = CreateDefaultName(Clock.GetLocalNow());
                        }

                        shell.OpenEditor(new EditorKey.Session(saved.SessionId));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        Log.ForContext<LiveSessionDetailViewModel>().Warning(e, "Failed to open saved live session {SessionId}", saved.SessionId);
                        ErrorMessages.Add(FailurePresentation.From(LiveSessionSaveFailureReason.Cleanup, e.ToString()));
                    }
                    break;

                case LiveSessionSaveResult.ProcessingFailed failed:
                    ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                    break;
                case LiveSessionSaveResult.Failed failed:
                    ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.ForContext<LiveSessionDetailViewModel>().Error(e, "Failed to submit live capture save");
            ErrorMessages.Add(FailurePresentation.From(LiveSessionSaveFailureReason.Save, e.ToString()));
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

    private void ApplyRuntimeSignalPreferences(SignalDisplayPreferences preferences)
    {
        var previous = signalsWorkspace.SignalDisplayPreferences;
        signalsWorkspace.ApplySignalDisplayPreferences(preferences);
        PreferencesPage.RefreshSignalDisplayPreferences(previous, preferences);
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
            signalsWorkspace.SignalLayoutPreferences);
    }

    private static string CreateDefaultName(DateTimeOffset localTime)
    {
        return $"Live Session {localTime.DateTime:dd-MM-yyyy HH:mm:ss}";
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
        if (batch.Analysis is { } analysis)
            UiThreadDispatcher.Post(() =>
            {
                if (isViewLoaded && IsTabActive && liveSessionService.Current.Analysis?.Revision == analysis.Revision)
                    ApplyLiveAnalysis(analysis);
            });
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
        var connectionFailure = LiveDaqMessages.ConnectionFailure(snapshot.Controls.ConnectionState);
        if (shownConnectionFailure != connectionFailure)
        {
            shownConnectionFailure = connectionFailure;
            if (connectionFailure is not null) ErrorMessages.Add(connectionFailure);
        }
        if (blockedSavedCaptureRevision != snapshot.CaptureRevision)
        {
            blockedSavedCaptureRevision = null;
        }

        signalsWorkspace.ApplySessionHeader(snapshot.Controls.SessionHeader);
        ApplySignalAvailability(snapshot.Controls.SessionHeader);
        mediaWorkspace.ApplySessionHeader(snapshot.Controls.SessionHeader);
        TelemetryData = snapshot.AnalysisTelemetry;
        ApplyLiveAnalysis(snapshot.Analysis);
        if (!ReferenceEquals(shownAnalysisFailure, snapshot.AnalysisFailure))
        {
            shownAnalysisFailure = snapshot.AnalysisFailure;
            if (shownAnalysisFailure is { } failure)
                ErrorMessages.Add(new FailurePresentation(failure.Message, failure.Details));
        }
        var trackTimelineContext = CreateLiveTrackTimelineContext(snapshot.Controls);
        signalsWorkspace.ApplyTrackPresentation(snapshot.SessionTrackPoints, trackTimelineContext);
        mediaWorkspace.SetTrackPoints(snapshot.SessionTrackPoints, trackTimelineContext);

        if (snapshot.Controls.SessionHeader is { } header)
        {
            Timestamp = header.SessionStartUtc.LocalDateTime;
        }

        ControlState = snapshot.Controls;
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

        bakeTasks.RemoveAll(task => task.IsCompleted);
        bakeTasks.Add(BakeCachePresentationAsync(service, telemetryData, dimensions, cts, cutoffs));
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
                if (cts.IsCancellationRequested || !ReferenceEquals(bakeCts, cts) || !isViewLoaded || !IsTabActive)
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
            Log.ForContext<LiveSessionDetailViewModel>().Warning(e, "Live analysis rendering failed for {IdentityKey}", IdentityKey);
            var failure = new LiveAnalysisFailure(LiveAnalysisFailureReason.Rendering, e.ToString());
            UiThreadDispatcher.Post(() =>
            {
                if (!cts.IsCancellationRequested && ReferenceEquals(bakeCts, cts) && isViewLoaded)
                    ErrorMessages.Add(new FailurePresentation(failure.Message, failure.Details));
            });
        }
        finally { cts.Dispose(); }
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

        ApplyDampingPercentages(analysisResults.Current?.DampingPercentages ?? SessionDampingPercentages.Empty);

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
        if (persistedDampingSpeedCutoffs is null)
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
        if (persistedDampingSpeedCutoffs is not { } draftBase || bikeCoordinator is null)
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
            BikeId,
            draftBase.Get(side, circuit),
            side,
            circuit,
            committedCutoffs.Get(side, circuit));

        switch (result)
        {
            case BikeDampingSpeedCutoffUpdateResult.Saved saved:
                ApplyPersistedDampingSpeedCutoffs(saved.Snapshot.DampingSpeedCutoffs);
                break;

            case BikeDampingSpeedCutoffUpdateResult.Conflict conflict:
                ApplyPersistedDampingSpeedCutoffs(conflict.CurrentSnapshot.DampingSpeedCutoffs);
                ErrorMessages.Add(new FailurePresentation("Bike damping cutoff changed elsewhere. Reloaded the latest cutoff."));
                break;

            case BikeDampingSpeedCutoffUpdateResult.Missing:
                persistedDampingSpeedCutoffs = null;
                OnPropertyChanged(nameof(CanEditDampingSpeedCutoffs));
                DampingSpeedCutoffs = draftBase;
                PlotDampingSpeedCutoffs = draftBase;
                break;

            case BikeDampingSpeedCutoffUpdateResult.Failed failed:
                DampingSpeedCutoffs = draftBase;
                PlotDampingSpeedCutoffs = draftBase;
                ErrorMessages.Add(FailurePresentation.From(failed.Reason, failed.Details));
                break;
        }
    }

    private void ApplyPersistedDampingSpeedCutoffs(DampingSpeedCutoffs cutoffs)
    {
        persistedDampingSpeedCutoffs = cutoffs.ClampValues();
        dampingSpeedCutoffPreviewOrigin = null;
        OnPropertyChanged(nameof(CanEditDampingSpeedCutoffs));
        PlotDampingSpeedCutoffs = persistedDampingSpeedCutoffs;
        DampingSpeedCutoffs = persistedDampingSpeedCutoffs;
    }

    private LiveAnalysisOptions CurrentAnalysisOptions => new(SelectedTravelDistributionMode,
        SelectedVelocityAverageMode, SelectedBalanceDisplacementMode, SelectedBalanceSpeedMode,
        DampingSpeedCutoffs, PlotDampingSpeedCutoffs);

    private void UpdateAnalysisOptions()
    {
        analysisResults.Apply(null);
        ApplyDampingPercentages(SessionDampingPercentages.Empty);
        liveSessionService.UpdateAnalysisOptions(CurrentAnalysisOptions);
        NotifyAnalysisStates();
    }

    private void ApplyLiveAnalysis(LiveSessionAnalysis? analysis)
    {
        if (analysis is not null && analysis.Options != CurrentAnalysisOptions) return;
        analysisResults.Apply(analysis);
        ApplyDampingPercentages(analysis?.DampingPercentages ?? SessionDampingPercentages.Empty);
        NotifyAnalysisStates();
    }

    private void NotifyAnalysisStates()
    {
        OnPropertyChanged(nameof(FrontAnalysisState));
        OnPropertyChanged(nameof(RearAnalysisState));
        OnPropertyChanged(nameof(CompressionBalanceState));
        OnPropertyChanged(nameof(ReboundBalanceState));
    }

    partial void OnDampingSpeedCutoffsChanged(DampingSpeedCutoffs value)
    {
        UpdateAnalysisOptions();
    }

    partial void OnPlotDampingSpeedCutoffsChanged(DampingSpeedCutoffs value)
    {
        UpdateAnalysisOptions();
        lastBakedTelemetryData = null;
        MaybeQueueBake(TelemetryData);
    }

    private bool IsCurrentCaptureAlreadySaved()
    {
        return blockedSavedCaptureRevision is not null;
    }

    private void RefreshUi()
    {
        ApplyPendingPresentation();
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

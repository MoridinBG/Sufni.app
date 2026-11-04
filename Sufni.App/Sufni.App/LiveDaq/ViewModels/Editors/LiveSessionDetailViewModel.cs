using Sufni.App.Sessions.Presentation;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.ViewModels.SessionPages;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Models;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels.Base;

namespace Sufni.App.LiveDaq.ViewModels.Editors;

public sealed partial class LiveSessionDetailViewModel : TabPageViewModelBase, ILiveSessionControlsWorkspace
{
    private readonly LiveSessionSignalsWorkspaceViewModel signalsWorkspace;
    private readonly LiveSessionMediaWorkspaceViewModel mediaWorkspace;
    private readonly ILiveCaptureCoordinator liveCaptureCoordinator;
    private readonly ILiveDaqCoordinator liveDaqCoordinator;
    private readonly LivePagePresentationApplier pageApplier;
    private bool hasLoaded;
    private bool isViewLoaded;
    private long? blockedSavedCaptureRevision;
    private LiveAnalysisFailure? shownAnalysisFailure;
    private FailurePresentation? shownConnectionFailure;

    public string IdentityKey { get; }
    public Guid SetupId { get; }
    public string? SetupName { get; }
    public Guid BikeId { get; }
    public string? BikeName { get; }
    public ILiveSessionSignalsWorkspace SignalsWorkspace => signalsWorkspace;
    public ISessionMediaWorkspace MediaWorkspace => mediaWorkspace;
    public LiveSessionAnalysisWorkspaceViewModel AnalysisWorkspace { get; }
    public LiveSessionShellMobileWorkspaceViewModel ShellMobileWorkspace { get; }
    public ISessionSidebarWorkspace SidebarWorkspace { get; }
    public NotesPageViewModel NotesPage { get; } = new();
    public PreferencesPageViewModel PreferencesPage { get; }
    public SpringPageViewModel SpringPage { get; }
    public DampingPageViewModel DampingPage { get; }
    public BalancePageViewModel BalancePage { get; }
    public LiveSignalsPageViewModel LiveSignalsPage { get; }
    public ObservableCollection<PageViewModelBase> Pages { get; }
    public SuspensionSettings ForkSettings => NotesPage.ForkSettings;
    public SuspensionSettings ShockSettings => NotesPage.ShockSettings;
    public string? DescriptionText { get => NotesPage.Description; set => NotesPage.Description = value; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectionStateText))]
    [NotifyPropertyChangedFor(nameof(CaptureDurationText))]
    [NotifyPropertyChangedFor(nameof(LastError))]
    [NotifyPropertyChangedFor(nameof(HasLastError))]
    [NotifyPropertyChangedFor(nameof(HasSessionHeader))]
    [NotifyPropertyChangedFor(nameof(SessionIdText))]
    [NotifyPropertyChangedFor(nameof(AcceptedTravelRateText))]
    [NotifyPropertyChangedFor(nameof(AcceptedImuRateText))]
    [NotifyPropertyChangedFor(nameof(AcceptedGpsRateText))]
    public partial LiveCaptureControls ControlState { get; private set; } = LiveCaptureControls.Empty;

    public string ConnectionStateText => $"State: {LiveDaqUiSnapshot.ToConnectionStateText(ControlState.ConnectionState)}";
    public string CaptureDurationText => $"Capture: {ControlState.CaptureDuration:g}";
    public string? LastError => ControlState.LastError ?? (ControlState.Phase == LiveCapturePhase.AwaitingReset ? "DAQ started a new live session." : null);
    public bool HasLastError => !string.IsNullOrWhiteSpace(LastError);
    public bool HasSessionHeader => ControlState.SessionHeader is not null;
    public string SessionIdText => ControlState.SessionHeader is { } header ? $"Session: {header.SessionId}" : "Session: -";
    public string AcceptedTravelRateText => LiveProtocolHelpers.FormatRateText("Travel", ControlState.SessionHeader?.AcceptedTravelRateMhz);
    public string AcceptedImuRateText => LiveProtocolHelpers.FormatRateText("IMU", ControlState.SessionHeader?.AcceptedImuRateMhz);
    public string AcceptedGpsRateText => LiveProtocolHelpers.FormatRateText("GPS", ControlState.SessionHeader?.AcceptedGpsRateMhz);

    public LiveSessionDetailViewModel(
        LiveDaqSessionContext context,
        ILiveCaptureCoordinator liveCaptureCoordinator,
        ILiveDaqCoordinator liveDaqCoordinator,
        ISessionPresentationService sessionPresentationService,
        IBackgroundTaskRunner backgroundTaskRunner,
        IMapViewModelFactory mapViewModelFactory,
        IShellCoordinator shell,
        IDialogService dialogService,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider,
        IBikeStore bikeStore,
        IBikeCoordinator bikeCoordinator)
        : base(shell, dialogService, uiThreadDispatcher, timeProvider: timeProvider)
    {
        IdentityKey = context.IdentityKey;
        SetupId = context.SetupId;
        SetupName = context.SetupName;
        BikeId = context.BikeId;
        BikeName = context.BikeName;
        this.liveCaptureCoordinator = liveCaptureCoordinator;
        this.liveDaqCoordinator = liveDaqCoordinator;

        var timeline = new SessionTimelineLinkViewModel();
        signalsWorkspace = new LiveSessionSignalsWorkspaceViewModel(timeline, CreatePlotRanges(context), liveCaptureCoordinator.SignalBatches);
        PreferencesPage = new PreferencesPageViewModel(() => signalsWorkspace.SignalDisplayPreferences, ApplyRuntimeSignalPreferences);
        mediaWorkspace = new LiveSessionMediaWorkspaceViewModel(mapViewModelFactory, timeline);
        Name = CreateDefaultName(Clock.GetLocalNow());
        LiveSignalsPage = new LiveSignalsPageViewModel(signalsWorkspace, mediaWorkspace);
        AnalysisWorkspace = new LiveSessionAnalysisWorkspaceViewModel(context, liveCaptureCoordinator,
            bikeStore, bikeCoordinator, error => ErrorMessages.Add(error));
        SpringPage = new SpringPageViewModel(AnalysisWorkspace);
        DampingPage = new DampingPageViewModel(AnalysisWorkspace);
        BalancePage = new BalancePageViewModel(AnalysisWorkspace);
        Pages = [LiveSignalsPage, SpringPage, DampingPage, NotesPage, PreferencesPage];
        ShellMobileWorkspace = new LiveSessionShellMobileWorkspaceViewModel(this, Pages);
        SidebarWorkspace = new SessionSidebarWorkspaceViewModel(this, () => Name, value => Name = value,
            NotesPage, PreferencesPage, SaveCommand, ResetCommand);
        pageApplier = new LivePagePresentationApplier(AnalysisWorkspace, sessionPresentationService,
            backgroundTaskRunner, uiThreadDispatcher, () => isViewLoaded && IsTabActive,
            error => ErrorMessages.Add(error), IdentityKey, SpringPage, DampingPage, BalancePage, NotesPage, Pages);
        WireNotesPageForwarding();

        ApplyPresentation(liveCaptureCoordinator.Current);

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
            pageApplier.SetDimensions(dimensions.Value);
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
        var start = await liveCaptureCoordinator.AttachAsync();
        if (start is LivePreviewStartResult.Busy busy) Notifications.Add(LiveDaqMessages.Busy(busy.Holder));
        if (start is LivePreviewStartResult.Unavailable unavailable) Notifications.Add(unavailable.Message);

        if (isViewLoaded && IsTabActive)
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
        EnsureScopedSubscription(disposables =>
            disposables.Add(liveCaptureCoordinator.States.Subscribe(ApplyPresentation)));
        pageApplier.Refresh();
    }

    private void StopForegroundUpdates()
    {
        DisposeScopedSubscriptions();
        pageApplier.Suspend();
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
        isViewLoaded = false;
        StopForegroundUpdates();
        AnalysisWorkspace.Dispose();
        ShellMobileWorkspace.Dispose();
        await liveCaptureCoordinator.DisposeAsync();
        await pageApplier.DisposeAsync();
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
        await liveCaptureCoordinator.ResetAsync();
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

            var result = await liveDaqCoordinator.SaveCaptureAsync(SessionSnapshot.From(session), liveCaptureCoordinator, CreateCurrentSessionPreferences());
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
                AnalysisWorkspace.SelectedTravelDistributionMode,
                AnalysisWorkspace.SelectedVelocityAverageMode,
                AnalysisWorkspace.SelectedBalanceDisplacementMode,
                AnalysisWorkspace.SelectedBalanceSpeedMode,
                AnalysisWorkspace.SelectedSessionInsightsTargetProfile),
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

    private void ApplyPresentation(LiveCaptureState state)
    {
        var connectionFailure = LiveDaqMessages.ConnectionFailure(state.Controls.ConnectionState);
        if (shownConnectionFailure != connectionFailure)
        {
            shownConnectionFailure = connectionFailure;
            if (connectionFailure is not null) ErrorMessages.Add(connectionFailure);
        }
        ControlState = state.Controls;
        if (state.Controls.SessionHeader is { } header) Timestamp = header.SessionStartUtc.LocalDateTime;
        ApplySignalAvailability(state.Controls.SessionHeader);
        if (!ReferenceEquals(shownAnalysisFailure, state.AnalysisFailure))
        {
            shownAnalysisFailure = state.AnalysisFailure;
            if (shownAnalysisFailure is { } failure)
                ErrorMessages.Add(new FailurePresentation(
                    "Live analysis could not be calculated. Capture continues; new samples will retry analysis.", failure.Details));
        }
        EvaluateDirtiness();
        RefreshCommandState();
        signalsWorkspace.ApplySessionHeader(state.Controls.SessionHeader);
        signalsWorkspace.ApplySignalPresence(state.SignalPresence);
        mediaWorkspace.ApplySessionHeader(state.Controls.SessionHeader);
        var trackContext = CreateLiveTrackTimelineContext(state.Controls);
        signalsWorkspace.ApplyTrackPresentation(state.TrackPoints, trackContext);
        mediaWorkspace.SetTrackPoints(state.TrackPoints, trackContext);
        AnalysisWorkspace.Apply(state);
        pageApplier.Refresh();
    }

    private static TrackTimeRange? CreateLiveTrackTimelineContext(LiveCaptureControls controls)
    {
        var durationSeconds = controls.CaptureDuration.TotalSeconds;
        return controls.CaptureStartUtc is { } captureStartUtc
               && double.IsFinite(durationSeconds)
               && durationSeconds > 0
            ? new TrackTimeRange(captureStartUtc.ToUnixTimeSeconds(), durationSeconds)
            : null;
    }

    private bool IsCurrentCaptureAlreadySaved() => blockedSavedCaptureRevision == liveCaptureCoordinator.Current.CaptureRevision;

    private void RefreshCommandState()
    {
        SaveCommand.NotifyCanExecuteChanged();
        ResetCommand.NotifyCanExecuteChanged();
    }

    private void ResetCapturePresentation()
    {
        pageApplier.Clear();
        signalsWorkspace.Timeline.Reset();
    }
}

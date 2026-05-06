using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Globalization;
using Avalonia;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.SessionGraph;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.Tests.ViewModels.Editors;

public class SessionDetailViewModelTests
{
    private readonly SessionCoordinator sessionCoordinator = TestCoordinatorSubstitutes.Session();
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly IRecordedSessionGraph recordedSessionGraph = Substitute.For<IRecordedSessionGraph>();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly ISessionAnalysisService sessionAnalysisService = Substitute.For<ISessionAnalysisService>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    public SessionDetailViewModelTests()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        sessionPresentationService.CalculateDamperPercentages(Arg.Any<TelemetryData>(), Arg.Any<TelemetryTimeRange?>())
            .Returns(new SessionDamperPercentages(null, null, null, null, null, null, null, null));
        sessionAnalysisService.Analyze(Arg.Any<SessionAnalysisRequest>()).Returns(SessionAnalysisResult.Hidden);
    }

    private SessionDetailViewModel CreateEditor(
        SessionSnapshot snapshot,
        IObservable<RecordedSessionDomainSnapshot>? watch = null,
        bool? isDesktop = null,
        ISessionPreferences? sessionPreferences = null)
    {
        if (isDesktop.HasValue)
        {
            TestApp.SetIsDesktop(isDesktop.Value);
        }

        recordedSessionGraph.WatchSession(snapshot.Id).Returns(watch ?? Observable.Empty<RecordedSessionDomainSnapshot>());
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        var preferencesService = sessionPreferences ?? CreateSessionPreferences();
        return new SessionDetailViewModel(
            snapshot,
            sessionCoordinator,
            sessionStore,
            recordedSessionGraph,
            sessionPresentationService,
            sessionAnalysisService,
            tileLayerService,
            shell,
            dialogService,
            preferencesService);
    }

    private void SetDesktop(bool isDesktop)
    {
        TestApp.SetIsDesktop(isDesktop);
    }

    // ----- Construction -----

    [AvaloniaFact]
    public void Construction_FromSnapshot_PopulatesFields()
    {
        var snapshot = TestSnapshots.Session(
            name: "trail run",
            description: "first lap",
            timestamp: 1700000000,
            hasProcessedData: true,
            updated: 9);

        var editor = CreateEditor(snapshot);

        Assert.Equal(snapshot.Id, editor.Id);
        Assert.Equal("trail run", editor.Name);
        Assert.Equal("first lap", editor.NotesPage.Description);
        Assert.NotNull(editor.Timestamp);
        Assert.True(editor.IsComplete);
        Assert.Equal(9, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public void Construction_WhenProcessedDataExists_InitializesLoadingSurfaceStates()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true) with
        {
            FullTrackId = Guid.NewGuid(),
        };

        var editor = CreateEditor(snapshot);

        Assert.Equal(SurfaceStateKind.Loading, editor.TravelGraphState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.VelocityGraphState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.ImuGraphState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.FrontStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.RearStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.CompressionBalanceState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.ReboundBalanceState.Kind);
        Assert.True(editor.FrontForkVibrationState.IsHidden);
        Assert.True(editor.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.RearForkVibrationState.IsHidden);
        Assert.True(editor.RearFrameVibrationState.IsHidden);
        Assert.Equal(SurfaceStateKind.Loading, editor.MapState.Kind);
        Assert.True(editor.ScreenState.IsReady);
    }

    [AvaloniaFact]
    public void Construction_InitializesStatisticsModeDefaultsAndOptions()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);

        var editor = CreateEditor(snapshot);

        Assert.Equal(TravelHistogramMode.ActiveSuspension, editor.SelectedTravelHistogramMode);
        Assert.Equal(BalanceDisplacementMode.Zenith, editor.SelectedBalanceDisplacementMode);
        Assert.Equal(VelocityAverageMode.SampleAveraged, editor.SelectedVelocityAverageMode);
        Assert.Equal(SessionAnalysisTargetProfile.Trail, editor.SelectedSessionAnalysisTargetProfile);
        Assert.Equal([TravelHistogramMode.ActiveSuspension, TravelHistogramMode.DynamicSag], editor.TravelHistogramModeOptions.Select(option => option.Value));
        Assert.Equal([BalanceDisplacementMode.Zenith, BalanceDisplacementMode.Travel], editor.BalanceDisplacementModeOptions.Select(option => option.Value));
        Assert.Equal([VelocityAverageMode.SampleAveraged, VelocityAverageMode.StrokePeakAveraged], editor.VelocityAverageModeOptions.Select(option => option.Value));
        Assert.Equal([SessionAnalysisTargetProfile.Weekend, SessionAnalysisTargetProfile.Trail, SessionAnalysisTargetProfile.Enduro, SessionAnalysisTargetProfile.DH], editor.SessionAnalysisTargetProfileOptions.Select(option => option.Value));
        Assert.All(editor.TravelHistogramModeOptions, option => Assert.False(string.IsNullOrWhiteSpace(option.Description)));
        Assert.All(editor.BalanceDisplacementModeOptions, option => Assert.False(string.IsNullOrWhiteSpace(option.Description)));
        Assert.All(editor.VelocityAverageModeOptions, option => Assert.False(string.IsNullOrWhiteSpace(option.Description)));
        Assert.All(editor.SessionAnalysisTargetProfileOptions, option => Assert.False(string.IsNullOrWhiteSpace(option.Description)));
        Assert.Equal("Travel: Active suspension  Velocity: Sample-averaged  Balance: Zenith", editor.SessionAnalysisModesText);
    }

    [AvaloniaFact]
    public void SessionAnalysisContextText_UsesDisplayNamesAndInvariantRangeFormatting()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
            editor.TelemetryData = TestTelemetryData.Create();
            editor.SelectedTravelHistogramMode = TravelHistogramMode.DynamicSag;
            editor.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;
            editor.SelectedBalanceDisplacementMode = BalanceDisplacementMode.Travel;

            editor.SetAnalysisRange(0.02, 0.16);

            Assert.Equal("Selected range 0.0-0.2s", editor.SessionAnalysisRangeText);
            Assert.Equal("Travel: Dynamic sag  Velocity: Stroke-peak average  Balance: Travel", editor.SessionAnalysisModesText);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    // ----- Dirtiness -----

    [AvaloniaFact]
    public void EditingName_MakesSaveCommandExecutable()
    {
        var snapshot = TestSnapshots.Session();
        var editor = CreateEditor(snapshot);
        Assert.False(editor.SaveCommand.CanExecute(null));

        editor.Name = "renamed";

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void EditingForkSpringRate_MakesSaveCommandExecutable()
    {
        var snapshot = TestSnapshots.Session();
        var editor = CreateEditor(snapshot);

        editor.NotesPage.ForkSettings.SpringRate = "550 lb/in";

        Assert.True(editor.SaveCommand.CanExecute(null));
    }

    // ----- Save -----

    [AvaloniaFact]
    public async Task Save_HappyPath_RoutesThroughCoordinator_AndUpdatesBaseline()
    {
        var snapshot = TestSnapshots.Session(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Saved(11));
        SetDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        await sessionCoordinator.Received(1).SaveAsync(
            Arg.Is<Session>(s => s.Id == snapshot.Id && s.Name == "renamed"),
            5);
        Assert.Equal(11, editor.BaselineUpdated);
        Assert.False(editor.IsDirty);
        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Save_OnMobile_DoesNotNavigateDirectly()
    {
        var snapshot = TestSnapshots.Session(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Saved(11));
        SetDesktop(false);

        await editor.SaveCommand.ExecuteAsync(null);

        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task Save_OnConflict_PromptsUser_AndReloadsWhenAccepted()
    {
        var snapshot = TestSnapshots.Session(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Session(id: snapshot.Id, name: "remote-updated", updated: 12);
        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Conflict(fresh));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        SetDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("remote-updated", editor.Name);
        Assert.Equal(12, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task Save_OnConflict_DoesNothing_WhenUserDeclinesReload()
    {
        var snapshot = TestSnapshots.Session(name: "old", updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        var fresh = TestSnapshots.Session(id: snapshot.Id, name: "remote-updated", updated: 12);
        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Conflict(fresh));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        SetDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal("renamed", editor.Name);
        Assert.Equal(5, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task Save_OnFailed_AppendsErrorMessage()
    {
        var snapshot = TestSnapshots.Session(updated: 5);
        var editor = CreateEditor(snapshot);
        editor.Name = "renamed";

        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Failed("disk full"));
        SetDesktop(true);

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Single(editor.ErrorMessages);
    }

    // ----- Delete -----

    [AvaloniaFact]
    public async Task Delete_HappyPath_NavigatesBack()
    {
        var snapshot = TestSnapshots.Session();
        var editor = CreateEditor(snapshot);
        sessionCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SessionDeleteResult(SessionDeleteOutcome.Deleted));

        await editor.DeleteCommand.ExecuteAsync(true);

        shell.Received(1).GoBack();
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Delete_Failed_AppendsErrorMessage_AndDoesNotNavigateBack()
    {
        var snapshot = TestSnapshots.Session();
        var editor = CreateEditor(snapshot);
        sessionCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SessionDeleteResult(SessionDeleteOutcome.Failed, "locked"));

        await editor.DeleteCommand.ExecuteAsync(true);

        Assert.Single(editor.ErrorMessages);
        shell.DidNotReceive().GoBack();
    }

    // ----- Load / unload -----

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_AppliesCoordinatorResult()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.Create();
        var trackPoints = new List<TrackPoint> { new(1, 1, 1, 0) };
        var fullTrackPoints = new List<TrackPoint> { new(2, 2, 2, 0) };
        var result = new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            telemetry,
            Guid.NewGuid(),
            fullTrackPoints,
            trackPoints,
            400.0,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8)));
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(result);
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Same(telemetry, editor.TelemetryData);
        Assert.Same(trackPoints, editor.TrackPoints);
        Assert.Same(fullTrackPoints, editor.FullTrackPoints);
        Assert.Equal(400.0, editor.MapVideoWidth);
        Assert.Equal(1, editor.DamperPage.FrontHscPercentage);
        Assert.True(editor.IsComplete);
        Assert.Equal(SurfaceStateKind.Ready, editor.TravelGraphState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.VelocityGraphState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.ImuGraphState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.MapState.Kind);
        Assert.True(editor.FrontForkVibrationState.IsHidden);
        Assert.True(editor.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.RearForkVibrationState.IsHidden);
        Assert.True(editor.RearFrameVibrationState.IsHidden);
        Assert.True(editor.HasMediaContent);
        Assert.True(editor.ScreenState.IsReady);
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_AppliesPersistedPlotPreferences()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry();
        var preferences = Substitute.For<ISessionPreferences>();
        ConfigureRecordedPreferences(
            preferences,
            snapshot.Id,
            new SessionPreferences(
                new SessionPlotPreferences(Travel: true, Velocity: false, Imu: true),
                new SessionStatisticsPreferences()));
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.PreferencesPage.TravelPlot.Selected);
        Assert.False(editor.PreferencesPage.VelocityPlot.Selected);
        Assert.True(editor.PreferencesPage.ImuPlot.Selected);
        Assert.True(editor.PreferencesPage.TravelPlot.Available);
        Assert.True(editor.PreferencesPage.VelocityPlot.Available);
        Assert.True(editor.PreferencesPage.ImuPlot.Available);
        Assert.True(editor.TravelGraphState.IsReady);
        Assert.True(editor.VelocityGraphState.IsHidden);
        Assert.True(editor.ImuGraphState.IsReady);
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_DisablesAndHidesImuPreference_WhenTelemetryHasNoImu()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.Create()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.PreferencesPage.TravelPlot.Available);
        Assert.True(editor.PreferencesPage.VelocityPlot.Available);
        Assert.False(editor.PreferencesPage.ImuPlot.Available);
        Assert.True(editor.TravelGraphState.IsReady);
        Assert.True(editor.VelocityGraphState.IsReady);
        Assert.True(editor.ImuGraphState.IsHidden);
    }

    [AvaloniaFact]
    public async Task PlotPreferenceChange_PersistsAndUpdatesGraphStatesWithoutDirtyingSession()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        Func<SessionPreferences, SessionPreferences>? update = null;
        preferences.UpdateRecordedAsync(
                snapshot.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(CreateVibrationTelemetry()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();

        editor.PreferencesPage.VelocityPlot.Selected = false;

        Assert.False(editor.IsDirty);
        Assert.True(editor.TravelGraphState.IsReady);
        Assert.True(editor.VelocityGraphState.IsHidden);
        Assert.True(editor.ImuGraphState.IsReady);
        await preferences.Received(1).UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        Assert.NotNull(update);
        Assert.False(update!(SessionPreferences.Default).Plots.Velocity);
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_AppliesPersistedStatisticsWithoutSavingDuringHydration()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>();
        ConfigureRecordedPreferences(
            preferences,
            snapshot.Id,
            new SessionPreferences(
                new SessionPlotPreferences(),
                new SessionStatisticsPreferences(
                    TravelHistogramMode.DynamicSag,
                    VelocityAverageMode.StrokePeakAveraged,
                    BalanceDisplacementMode.Travel,
                    SessionAnalysisTargetProfile.DH)));
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.Create()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(TravelHistogramMode.DynamicSag, editor.SelectedTravelHistogramMode);
        Assert.Equal(VelocityAverageMode.StrokePeakAveraged, editor.SelectedVelocityAverageMode);
        Assert.Equal(BalanceDisplacementMode.Travel, editor.SelectedBalanceDisplacementMode);
        Assert.Equal(SessionAnalysisTargetProfile.DH, editor.SelectedSessionAnalysisTargetProfile);
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
    }

    [AvaloniaFact]
    public async Task StatisticsPreferenceChange_RecomputesAnalysisAndPersistsAfterHydration()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        Func<SessionPreferences, SessionPreferences>? update = null;
        preferences.UpdateRecordedAsync(
                snapshot.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.Create()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();
        sessionAnalysisService.ClearReceivedCalls();

        editor.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;

        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionAnalysisRequest>(request =>
            request.VelocityAverageMode == VelocityAverageMode.StrokePeakAveraged));
        await preferences.Received(1).UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        Assert.NotNull(update);
        Assert.Equal(
            VelocityAverageMode.StrokePeakAveraged,
            update!(SessionPreferences.Default).Statistics.VelocityAverageMode);
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_AppliesFreshSessionAnalysis()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.Create();
        var damperPercentages = new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var analysis = CreateAnalysisResult();
        var result = new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            telemetry,
            FullTrackId: null,
            FullTrackPoints: null,
            TrackPoints: null,
            MapVideoWidth: null,
            damperPercentages));
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>()).Returns(result);
        sessionAnalysisService.Analyze(Arg.Any<SessionAnalysisRequest>()).Returns(analysis);
        sessionAnalysisService.ClearReceivedCalls();
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Same(analysis, editor.SessionAnalysis);
        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionAnalysisRequest>(request =>
            ReferenceEquals(request.TelemetryData, telemetry) &&
            request.DamperPercentages == damperPercentages));
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_WithForkAndFrameImu_SetsAllVibrationStatesReady()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = CreateVibrationTelemetry();
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.FrontForkVibrationState.IsReady);
        Assert.True(editor.FrontFrameVibrationState.IsReady);
        Assert.True(editor.RearForkVibrationState.IsReady);
        Assert.True(editor.RearFrameVibrationState.IsReady);
        Assert.Equal(SurfaceStateKind.Ready, editor.FrontStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.RearStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.CompressionBalanceState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.ReboundBalanceState.Kind);
    }

    [AvaloniaFact]
    public void SetAnalysisRange_RecomputesDamperPercentagesWithoutMarkingDirty()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry();
        var rangePercentages = new SessionDamperPercentages(10, 20, 30, 40, 50, 60, 70, 80);
        sessionPresentationService
            .CalculateDamperPercentages(
                telemetry,
                Arg.Is<TelemetryTimeRange?>(range =>
                    range.HasValue &&
                    range.Value.StartSeconds == 0.02 &&
                    range.Value.EndSeconds == 0.16))
            .Returns(rangePercentages);

        var editor = CreateEditor(snapshot);
        editor.TelemetryData = telemetry;

        editor.SetAnalysisRange(0.02, 0.16);

        Assert.Equal(0.02, editor.AnalysisRange?.StartSeconds);
        Assert.Equal(0.16, editor.AnalysisRange?.EndSeconds);
        Assert.Equal(10, editor.DamperPage.FrontHscPercentage);
        Assert.False(editor.IsDirty);
    }

    [AvaloniaFact]
    public void SetAnalysisRange_RecomputesAnalysisWithFreshDamperPercentagesOnce()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry();
        var rangePercentages = new SessionDamperPercentages(10, 20, 30, 40, 50, 60, 70, 80);
        sessionPresentationService
            .CalculateDamperPercentages(telemetry, Arg.Is<TelemetryTimeRange?>(range => range.HasValue))
            .Returns(rangePercentages);

        var editor = CreateEditor(snapshot);
        editor.TelemetryData = telemetry;
        sessionAnalysisService.ClearReceivedCalls();

        editor.SetAnalysisRange(0.02, 0.16);

        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionAnalysisRequest>(request =>
            request.AnalysisRange.HasValue &&
            request.DamperPercentages == rangePercentages));
    }

    [AvaloniaFact]
    public void ClearAnalysisRange_RecomputesDamperPercentagesForFullSession()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry();
        var rangePercentages = new SessionDamperPercentages(10, 20, 30, 40, 50, 60, 70, 80);
        var fullSessionPercentages = new SessionDamperPercentages(11, 21, 31, 41, 51, 61, 71, 81);
        sessionPresentationService
            .CalculateDamperPercentages(telemetry, Arg.Is<TelemetryTimeRange?>(range => range.HasValue))
            .Returns(rangePercentages);
        sessionPresentationService
            .CalculateDamperPercentages(telemetry, Arg.Is<TelemetryTimeRange?>(range => !range.HasValue))
            .Returns(fullSessionPercentages);

        var editor = CreateEditor(snapshot);
        editor.TelemetryData = telemetry;
        editor.SetAnalysisRange(0.02, 0.16);

        editor.ClearAnalysisRange();

        Assert.Null(editor.AnalysisRange);
        Assert.Equal(11, editor.DamperPage.FrontHscPercentage);
        Assert.False(editor.IsDirty);
    }

    [AvaloniaFact]
    public void SelectedTravelHistogramMode_RecomputesAnalysis()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        editor.TelemetryData = TestTelemetryData.Create();
        sessionAnalysisService.ClearReceivedCalls();

        editor.SelectedTravelHistogramMode = TravelHistogramMode.DynamicSag;

        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionAnalysisRequest>(request =>
            request.TravelHistogramMode == TravelHistogramMode.DynamicSag));
    }

    [AvaloniaFact]
    public void SelectedVelocityAverageMode_RecomputesAnalysis()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        editor.TelemetryData = TestTelemetryData.Create();
        sessionAnalysisService.ClearReceivedCalls();

        editor.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;

        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionAnalysisRequest>(request =>
            request.VelocityAverageMode == VelocityAverageMode.StrokePeakAveraged));
    }

    [AvaloniaFact]
    public void SelectedBalanceDisplacementMode_RecomputesAnalysis()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        editor.TelemetryData = TestTelemetryData.Create();
        sessionAnalysisService.ClearReceivedCalls();

        editor.SelectedBalanceDisplacementMode = BalanceDisplacementMode.Travel;

        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionAnalysisRequest>(request =>
            request.BalanceDisplacementMode == BalanceDisplacementMode.Travel));
    }

    [AvaloniaFact]
    public void SelectedSessionAnalysisTargetProfile_RecomputesAnalysis()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        editor.TelemetryData = TestTelemetryData.Create();
        sessionAnalysisService.ClearReceivedCalls();

        editor.SelectedSessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Enduro;

        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionAnalysisRequest>(request =>
            request.TargetProfile == SessionAnalysisTargetProfile.Enduro));
    }

    [AvaloniaFact]
    public void DamperPercentagesChange_DoesNotIndependentlyRecomputeAnalysis()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        editor.TelemetryData = TestTelemetryData.Create();
        sessionAnalysisService.ClearReceivedCalls();

        editor.DamperPercentages = new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8);

        sessionAnalysisService.DidNotReceive().Analyze(Arg.Any<SessionAnalysisRequest>());
    }

    [AvaloniaFact]
    public void SetAnalysisRangeBoundaryFromMarker_UsesFirstAndSecondMarkerAsRangeBoundaries()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var editor = CreateEditor(snapshot);
        editor.TelemetryData = CreateVibrationTelemetry();

        editor.SetAnalysisRangeBoundaryFromMarker(0.02);
        Assert.Null(editor.AnalysisRange);

        editor.SetAnalysisRangeBoundaryFromMarker(0.16);

        Assert.Equal(0.02, editor.AnalysisRange?.StartSeconds);
        Assert.Equal(0.16, editor.AnalysisRange?.EndSeconds);
    }

    [AvaloniaFact]
    public void SetAnalysisRangeBoundaryFromMarker_ReplacesNearestBoundaryForExistingRange()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var editor = CreateEditor(snapshot);
        editor.TelemetryData = CreateVibrationTelemetry();
        editor.SetAnalysisRange(0.02, 0.18);

        editor.SetAnalysisRangeBoundaryFromMarker(0.05);

        Assert.Equal(0.05, editor.AnalysisRange?.StartSeconds);
        Assert.Equal(0.18, editor.AnalysisRange?.EndSeconds);
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_WithOnlyForkImu_HidesFrameVibrationStates()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = CreateVibrationTelemetry(frameImu: false);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.FrontForkVibrationState.IsReady);
        Assert.True(editor.RearForkVibrationState.IsReady);
        Assert.True(editor.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.RearFrameVibrationState.IsHidden);
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_WithoutImu_HidesVibrationStates()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = CreateVibrationTelemetry(forkImu: false, frameImu: false);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.FrontForkVibrationState.IsHidden);
        Assert.True(editor.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.RearForkVibrationState.IsHidden);
        Assert.True(editor.RearFrameVibrationState.IsHidden);
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_WithImuAndNoStrokes_WaitsForVibrationData()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = CreateVibrationTelemetry(frontStrokes: false, rearStrokes: false);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(SurfaceStateKind.WaitingForData, editor.FrontForkVibrationState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.FrontFrameVibrationState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.RearForkVibrationState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.RearFrameVibrationState.Kind);
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_WhenTelemetryLaterPending_ClearsVibrationStates()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry();
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(LoadedDesktopResult(telemetry)),
                Task.FromResult<SessionDesktopLoadResult>(new SessionDesktopLoadResult.TelemetryPending()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.FrontForkVibrationState.IsReady);

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.FrontForkVibrationState.IsHidden);
        Assert.True(editor.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.RearForkVibrationState.IsHidden);
        Assert.True(editor.RearFrameVibrationState.IsHidden);
    }

    [AvaloniaFact]
    public async Task Loaded_OnMobile_AppliesCacheResult_AndRemovesBalancePageWhenUnavailable()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var result = new SessionMobileLoadResult.LoadedFromCache(new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDamperPercentages(1, null, 2, null, 3, null, 4, null),
            false),
            TestTelemetryData.Create(),
            null);
        sessionCoordinator.LoadMobileDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        SetDesktop(false);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));
        var springPage = editor.Pages.OfType<SpringPageViewModel>().Single();

        Assert.NotNull(editor.TelemetryData);
        Assert.Equal("front-travel", springPage.FrontTravelHistogram);
        Assert.Equal("front-velocity", editor.DamperPage.FrontVelocityHistogram);
        Assert.True(editor.IsComplete);
        Assert.Equal(SurfaceStateKind.Ready, editor.TravelGraphState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.VelocityGraphState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.ImuGraphState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, springPage.FrontHistogramState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, springPage.RearHistogramState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.FrontStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.RearStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.CompressionBalanceState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.ReboundBalanceState.Kind);
        Assert.True(editor.FrontForkVibrationState.IsHidden);
        Assert.True(editor.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.RearForkVibrationState.IsHidden);
        Assert.True(editor.RearFrameVibrationState.IsHidden);
        Assert.False(editor.HasMediaContent);
        Assert.DoesNotContain(editor.Pages, page => page.DisplayName == "Balance");
    }

    [AvaloniaFact]
    public async Task Loaded_OnMobile_AppliesTrackPresentationToMapState()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var fullTrackPoints = new List<TrackPoint>
        {
            new(0, 0, 0, null),
            new(1, 100, 100, null),
        };
        var trackPoints = new List<TrackPoint>
        {
            new(0, 0, 0, null),
            new(1, 100, 100, null),
        };
        var result = new SessionMobileLoadResult.LoadedFromCache(new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDamperPercentages(1, null, 2, null, 3, null, 4, null),
            false),
            TestTelemetryData.Create(),
            new SessionTrackPresentationData(Guid.NewGuid(), fullTrackPoints, trackPoints, 400));
        sessionCoordinator.LoadMobileDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        SetDesktop(false);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));

        Assert.Same(trackPoints, editor.TrackPoints);
        Assert.Same(fullTrackPoints, editor.FullTrackPoints);
        Assert.Equal(400, editor.MapVideoWidth);
        Assert.Equal(SurfaceStateKind.Ready, editor.MapState.Kind);
        Assert.True(editor.HasMediaContent);
        Assert.Same(trackPoints, editor.MapViewModel!.SessionTrackPoints);
    }

    [AvaloniaFact]
    public async Task Loaded_WhenTelemetryPending_EntersWaitingStatesWithoutError()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionDesktopLoadResult.TelemetryPending());
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.False(editor.IsComplete);
        Assert.True(editor.ScreenState.IsReady);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.TravelGraphState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.VelocityGraphState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.ImuGraphState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.FrontStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, editor.RearStatisticsState.Kind);
        Assert.True(editor.FrontForkVibrationState.IsHidden);
        Assert.True(editor.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.RearForkVibrationState.IsHidden);
        Assert.True(editor.RearFrameVibrationState.IsHidden);
        Assert.Equal(SurfaceStateKind.Hidden, editor.MapState.Kind);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Loaded_WhenCoordinatorFails_SetsScreenError()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionDesktopLoadResult.Failed("boom"));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.ScreenState.IsError);
        Assert.Contains("boom", editor.ScreenState.Message);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Unloaded_CancelsInFlightLoad_AndDropsResult()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = TestTelemetryData.Create();
        var pending = new TaskCompletionSource<SessionDesktopLoadResult>();

        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(callInfo => AwaitWithCancellation(
                pending.Task,
                callInfo.ArgAt<CancellationToken>(1)));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        var loadTask = editor.LoadedCommand.ExecuteAsync(null);
        await Task.Yield();

        editor.UnloadedCommand.Execute(null);
        pending.SetResult(new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            telemetry,
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8))));

        await loadTask;

        Assert.Null(editor.TelemetryData);
        Assert.False(editor.IsComplete);
    }

    [AvaloniaFact]
    public async Task Unloaded_OnMobile_CancelsInFlightLoad_AndDropsCacheResult()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var pending = new TaskCompletionSource<SessionMobileLoadResult>();

        sessionCoordinator.LoadMobileDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => AwaitWithCancellation(
                pending.Task,
                callInfo.ArgAt<CancellationToken>(2)));
        SetDesktop(false);

        var editor = CreateEditor(snapshot);
        var loadTask = editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));
        await Task.Yield();

        editor.UnloadedCommand.Execute(null);
        pending.SetResult(new SessionMobileLoadResult.BuiltCache(new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDamperPercentages(1, null, 2, null, 3, null, 4, null),
            false),
            TestTelemetryData.Create(),
            new SessionTrackPresentationData(null, null, null, null)));

        await loadTask;

        var springPage = editor.Pages.OfType<SpringPageViewModel>().Single();
        Assert.Null(springPage.FrontTravelHistogram);
        Assert.Null(editor.DamperPage.FrontVelocityHistogram);
        Assert.False(editor.IsComplete);
    }

    [AvaloniaFact]
    public async Task SaveAfter_LoadedFromCacheWithNullTelemetry_PreservesHasProcessedData()
    {
        var snapshot = TestSnapshots.Session(updated: 5, hasProcessedData: true);
        var result = new SessionMobileLoadResult.LoadedFromCache(new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDamperPercentages(1, null, 2, null, 3, null, 4, null),
            false),
            null,
            null);
        sessionCoordinator.LoadMobileDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Saved(11));
        SetDesktop(false);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));
        editor.Name = "renamed";

        await editor.SaveCommand.ExecuteAsync(null);

        await sessionCoordinator.Received(1).SaveAsync(
            Arg.Is<Session>(session => session.Id == snapshot.Id && session.HasProcessedData),
            5);
    }

    [AvaloniaFact]
    public async Task LaterLoad_SupersedesEarlierCompletion()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var firstTelemetry = TestTelemetryData.Create();
        var secondTelemetry = TestTelemetryData.Create();
        var firstPending = new TaskCompletionSource<SessionDesktopLoadResult>();
        var callCount = 0;

        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return callCount == 1
                    ? AwaitWithCancellation(firstPending.Task, callInfo.ArgAt<CancellationToken>(1))
                    : Task.FromResult<SessionDesktopLoadResult>(new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
                        secondTelemetry,
                        null,
                        null,
                        null,
                        null,
                        new SessionDamperPercentages(10, 20, 30, 40, 50, 60, 70, 80))));
            });
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        var firstLoad = editor.LoadedCommand.ExecuteAsync(null);
        await Task.Yield();

        var secondLoad = editor.LoadedCommand.ExecuteAsync(null);
        firstPending.SetResult(new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            firstTelemetry,
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8))));

        await Task.WhenAll(firstLoad, secondLoad);

        Assert.Same(secondTelemetry, editor.TelemetryData);
        Assert.Equal(10, editor.DamperPage.FrontHscPercentage);
    }

    [AvaloniaFact]
    public async Task WatchRefreshes_AreCoalescedWhileLoadIsInFlight()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var initialResult = new SessionDesktopLoadResult.TelemetryPending();
        var refreshLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshPending = new TaskCompletionSource<SessionDesktopLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalPending = new TaskCompletionSource<SessionDesktopLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalTelemetry = TestTelemetryData.Create();
        var finalResultApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;

        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cancellationToken = callInfo.ArgAt<CancellationToken>(1);
                callCount++;
                return callCount switch
                {
                    1 => Task.FromResult<SessionDesktopLoadResult>(initialResult),
                    2 => StartRefreshLoad(refreshPending.Task, cancellationToken),
                    _ => StartFinalLoad(finalPending.Task, cancellationToken)
                };

                Task<SessionDesktopLoadResult> StartRefreshLoad(
                    Task<SessionDesktopLoadResult> task,
                    CancellationToken token)
                {
                    refreshLoadStarted.TrySetResult();
                    return AwaitWithCancellation(task, token);
                }

                Task<SessionDesktopLoadResult> StartFinalLoad(
                    Task<SessionDesktopLoadResult> task,
                    CancellationToken token)
                {
                    finalLoadStarted.TrySetResult();
                    return AwaitWithCancellation(task, token);
                }
            });
        SetDesktop(true);

        var editor = CreateEditor(snapshot, watch.AsObservable());

        void MarkWhenFinalStateApplied()
        {
            if (ReferenceEquals(editor.TelemetryData, finalTelemetry) &&
                editor.DamperPage.FrontHscPercentage == 1)
            {
                finalResultApplied.TrySetResult();
            }
        }

        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SessionDetailViewModel.TelemetryData))
            {
                MarkWhenFinalStateApplied();
            }
        };
        editor.DamperPage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DamperPageViewModel.FrontHscPercentage))
            {
                MarkWhenFinalStateApplied();
            }
        };

        await editor.LoadedCommand.ExecuteAsync(null);

        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        watch.OnNext(DomainFromSnapshot(snapshot with { HasProcessedData = true }, DerivedChangeKind.ProcessedDataAvailabilityChanged));
        await refreshLoadStarted.Task;
        watch.OnNext(DomainFromSnapshot(snapshot with { HasProcessedData = false }, DerivedChangeKind.ProcessedDataAvailabilityChanged));
        watch.OnNext(DomainFromSnapshot(snapshot with { HasProcessedData = true }, DerivedChangeKind.ProcessedDataAvailabilityChanged));
        refreshPending.SetResult(new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            TestTelemetryData.Create(),
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(9, 9, 9, 9, 9, 9, 9, 9))));

        await finalLoadStarted.Task;
        finalPending.SetResult(new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            finalTelemetry,
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8))));

        await finalResultApplied.Task;

        Assert.Same(finalTelemetry, editor.TelemetryData);
        await sessionCoordinator.Received(3).LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>());
        watch.Dispose();
    }

    [AvaloniaFact]
    public async Task Loaded_PromptsAndRecomputes_WhenInitialDomainIsStaleAndRecomputable()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", hasProcessedData: true, updated: 5);
        var recomputedSnapshot = snapshot with { Updated = 7 };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var recomputeCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionStore.Get(snapshot.Id).Returns(snapshot, snapshot);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionDesktopLoadResult.TelemetryPending());
        dialogService.ShowConfirmationAsync(
                "Session trail run has to be recomputed",
                "Recompute this session now?")
            .Returns(true);
        sessionCoordinator.RecomputeAsync(snapshot.Id, snapshot.Updated, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sessionStore.Get(snapshot.Id).Returns(recomputedSnapshot);
                recomputeCalled.TrySetResult();
                return new SessionRecomputeResult.Recomputed(recomputedSnapshot.Updated);
            });

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.Initial,
            new SessionStaleness.UnknownLegacyFingerprint()));

        await recomputeCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Yield();
        Assert.Equal(recomputedSnapshot.Updated, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task Loaded_AcceptedInitialRecompute_DoesNotPromptAgainForSameStaleDomain()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", hasProcessedData: true, updated: 5);
        var recomputedSnapshot = snapshot with { Updated = 7 };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var recomputeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recomputeResult = new TaskCompletionSource<SessionRecomputeResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleDomain = DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.Initial,
            new SessionStaleness.DependencyHashChanged());

        sessionStore.Get(snapshot.Id).Returns(snapshot, snapshot, recomputedSnapshot, recomputedSnapshot);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionDesktopLoadResult.TelemetryPending());
        dialogService.ShowConfirmationAsync(
                "Session trail run has to be recomputed",
                "Recompute this session now?")
            .Returns(true);
        sessionCoordinator.RecomputeAsync(snapshot.Id, snapshot.Updated, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                recomputeStarted.TrySetResult();
                return recomputeResult.Task;
            });

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);

        watch.OnNext(staleDomain);
        await recomputeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        watch.OnNext(staleDomain);
        watch.OnNext(staleDomain);
        await Task.Yield();

        sessionStore.Get(snapshot.Id).Returns(recomputedSnapshot);
        recomputeResult.SetResult(new SessionRecomputeResult.Recomputed(recomputedSnapshot.Updated));
        await WaitForAsync(() => editor.BaselineUpdated == recomputedSnapshot.Updated);

        watch.OnNext(staleDomain);
        await Task.Yield();

        await dialogService.Received(1).ShowConfirmationAsync(
            "Session trail run has to be recomputed",
            "Recompute this session now?");
        await sessionCoordinator.Received(1).RecomputeAsync(snapshot.Id, snapshot.Updated, Arg.Any<CancellationToken>());
        Assert.Equal(recomputedSnapshot.Updated, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task RecomputeSuccess_ClearsOldPresentationBeforeApplyingFreshTelemetry()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", hasProcessedData: true, updated: 5);
        var recomputedSnapshot = snapshot with { Updated = 7 };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var oldTelemetry = TestTelemetryData.Create();
        var freshTelemetry = TestTelemetryData.Create();
        var telemetryChanges = new List<TelemetryData?>();
        var loadCount = 0;

        sessionStore.Get(snapshot.Id).Returns(snapshot);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                loadCount++;
                return loadCount == 1
                    ? LoadedDesktopResult(oldTelemetry)
                    : LoadedDesktopResult(freshTelemetry);
            });
        dialogService.ShowConfirmationAsync(
                "Session trail run has to be recomputed",
                "Recompute this session now?")
            .Returns(true);
        sessionCoordinator.RecomputeAsync(snapshot.Id, snapshot.Updated, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sessionStore.Get(snapshot.Id).Returns(recomputedSnapshot);
                return new SessionRecomputeResult.Recomputed(recomputedSnapshot.Updated);
            });

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);
        Assert.Same(oldTelemetry, editor.TelemetryData);

        editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SessionDetailViewModel.TelemetryData))
            {
                telemetryChanges.Add(editor.TelemetryData);
            }
        };

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.Initial,
            new SessionStaleness.DependencyHashChanged()));

        await WaitForAsync(() => ReferenceEquals(editor.TelemetryData, freshTelemetry));

        Assert.Contains(null, telemetryChanges);
        Assert.Same(freshTelemetry, editor.TelemetryData);
        await sessionCoordinator.Received(2).LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task CloseCommand_DisposesGraphWatchAfterDeclinedStalePrompt()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", hasProcessedData: true, updated: 5);
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionDesktopLoadResult.TelemetryPending());
        dialogService.ShowConfirmationAsync(
                "Session trail run has to be recomputed",
                "Recompute this session now?")
            .Returns(false);

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.Initial,
            new SessionStaleness.DependencyHashChanged()));
        await Task.Yield();

        await dialogService.Received(1).ShowConfirmationAsync(
            "Session trail run has to be recomputed",
            "Recompute this session now?");

        await editor.CloseCommand.ExecuteAsync(null);
        shell.Received(1).Close(editor);

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.DependencyChanged,
            new SessionStaleness.DependencyHashChanged()));
        await Task.Yield();

        await dialogService.Received(1).ShowConfirmationAsync(
            "Session trail run has to be recomputed",
            "Recompute this session now?");
        await sessionCoordinator.DidNotReceive().RecomputeAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    [AvaloniaFact]
    public async Task Loaded_DoesNotReportError_WhenInitialDomainHasNoRawSource()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true, updated: 5);
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionDesktopLoadResult.TelemetryPending());

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.Initial,
            new SessionStaleness.MissingRawSource()));
        await Task.Yield();

        Assert.Empty(editor.ErrorMessages);
        await dialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
        await sessionCoordinator.DidNotReceive().RecomputeAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task RuntimeDerivedChange_RecomputeConfirmation_DiscardsDirtyDraft()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", description: "persisted", hasProcessedData: true, updated: 5);
        var recomputedSnapshot = snapshot with { Updated = 8 };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var recomputeCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionStore.Get(snapshot.Id).Returns(snapshot, snapshot, recomputedSnapshot, recomputedSnapshot);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionDesktopLoadResult.TelemetryPending());
        dialogService.ShowConfirmationAsync(
                "Session trail run has to be recomputed",
                Arg.Is<string>(message => message.Contains("discard unsaved changes", StringComparison.Ordinal)))
            .Returns(true);
        sessionCoordinator.RecomputeAsync(snapshot.Id, snapshot.Updated, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                sessionStore.Get(snapshot.Id).Returns(recomputedSnapshot);
                recomputeCalled.TrySetResult();
                return new SessionRecomputeResult.Recomputed(recomputedSnapshot.Updated);
            });

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        editor.DescriptionText = "dirty draft";
        Assert.True(editor.IsDirty);

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.DependencyChanged,
            new SessionStaleness.DependencyHashChanged()));

        await recomputeCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Yield();
        Assert.False(editor.IsDirty);
        Assert.Equal("persisted", editor.DescriptionText);
        Assert.Equal(recomputedSnapshot.Updated, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task RuntimeDerivedChange_DeclinedRecompute_KeepsDirtyDraft()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", description: "persisted", hasProcessedData: true, updated: 5);
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionDesktopLoadResult.TelemetryPending());
        dialogService.ShowConfirmationAsync("Session trail run has to be recomputed", Arg.Any<string>()).Returns(false);

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        editor.DescriptionText = "dirty draft";

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.FingerprintChanged,
            new SessionStaleness.DependencyHashChanged()));
        await Task.Yield();

        Assert.True(editor.IsDirty);
        Assert.Equal("dirty draft", editor.DescriptionText);
        await sessionCoordinator.DidNotReceive().RecomputeAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    private static RecordedSessionDomainSnapshot DomainFromSnapshot(
        SessionSnapshot snapshot,
        DerivedChangeKind changeKind = DerivedChangeKind.None,
        SessionStaleness? staleness = null) => new(
        snapshot,
        null,
        null,
        null,
        null,
        null,
        staleness ?? new SessionStaleness.Current(),
        changeKind);

    private static SessionDesktopLoadResult LoadedDesktopResult(TelemetryData telemetry)
    {
        return new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            telemetry,
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8)));
    }

    private static void ConfigureRecordedPreferences(
        ISessionPreferences preferences,
        Guid sessionId,
        SessionPreferences recordedPreferences)
    {
        preferences.GetRecordedAsync(sessionId).Returns(Task.FromResult(recordedPreferences));
        preferences.UpdateRecordedAsync(sessionId, Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(Task.CompletedTask);
        preferences.ClearReceivedCalls();
    }

    private static ISessionPreferences CreateSessionPreferences()
    {
        var preferences = Substitute.For<ISessionPreferences>();
        preferences.GetRecordedAsync(Arg.Any<Guid>()).Returns(Task.FromResult(SessionPreferences.Default));
        preferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(Task.CompletedTask);
        return preferences;
    }

    private static SessionAnalysisResult CreateAnalysisResult()
    {
        return new SessionAnalysisResult(
            SurfacePresentationState.Ready,
            [new SessionAnalysisFinding(
                SessionAnalysisCategory.DataQuality,
                SessionAnalysisSeverity.Info,
                SessionAnalysisConfidence.Low,
                "Analysis ready",
                "Telemetry was analyzed.",
                "Compare against the next run.",
                [])]);
    }

    private static TelemetryData CreateVibrationTelemetry(
        bool frontPresent = true,
        bool rearPresent = true,
        bool frontStrokes = true,
        bool rearStrokes = true,
        bool forkImu = true,
        bool frameImu = true)
    {
        return new TelemetryData
        {
            Metadata = new Metadata
            {
                SourceName = "v4-test.sst",
                Version = 4,
                SampleRate = 100,
                Timestamp = 1_700_000_000,
                Duration = 0.2,
            },
            Front = CreateSuspension(frontPresent, frontStrokes),
            Rear = CreateSuspension(rearPresent, rearStrokes),
            Airtimes = [],
            Markers = [],
            ImuData = CreateImuData(forkImu, frameImu),
        };
    }

    private static Suspension CreateSuspension(bool present, bool hasStrokes)
    {
        var travel = present
            ? Enumerable.Range(0, 20).Select(index => index <= 10 ? index * 12.0 : (20 - index) * 12.0).ToArray()
            : [];

        return new Suspension
        {
            Present = present,
            MaxTravel = present ? 200.0 : null,
            Travel = travel,
            Velocity = new double[travel.Length],
            TravelBins = Enumerable.Range(0, 21).Select(index => index * 10.0).ToArray(),
            VelocityBins = [],
            FineVelocityBins = [],
            Strokes = new Strokes
            {
                Compressions = present && hasStrokes
                    ? [CreateStroke(0, 4, 60.0, 500.0), CreateStroke(5, 9, 120.0, 900.0)]
                    : [],
                Rebounds = present && hasStrokes
                    ? [CreateStroke(10, 14, 110.0, -450.0), CreateStroke(15, 19, 50.0, -750.0)]
                    : [],
            },
        };
    }

    private static Stroke CreateStroke(int start, int end, double maxTravel, double maxVelocity)
    {
        return new Stroke
        {
            Start = start,
            End = end,
            Stat = new StrokeStat
            {
                MaxTravel = maxTravel,
                MaxVelocity = maxVelocity,
                Count = end - start + 1,
            },
            DigitizedTravel = [],
            DigitizedVelocity = [],
            FineDigitizedVelocity = [],
        };
    }

    private static RawImuData CreateImuData(bool forkImu, bool frameImu)
    {
        var activeLocations = new List<byte>();
        if (frameImu)
        {
            activeLocations.Add((byte)ImuLocation.Frame);
        }

        if (forkImu)
        {
            activeLocations.Add((byte)ImuLocation.Fork);
        }

        var records = new List<ImuRecord>();
        for (var sample = 0; sample < 20; sample++)
        {
            foreach (var _ in activeLocations)
            {
                records.Add(new ImuRecord(0, 0, 8192, 0, 0, 0));
            }
        }

        return new RawImuData
        {
            SampleRate = 100,
            ActiveLocations = activeLocations,
            Meta = activeLocations.Select(location => new ImuMetaEntry(location, 8192, 16.4f)).ToList(),
            Records = records,
        };
    }

    private static async Task<T> AwaitWithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
    {
        return await task.WaitAsync(cancellationToken);
    }
}

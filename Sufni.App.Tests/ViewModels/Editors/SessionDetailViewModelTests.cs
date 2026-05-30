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
using Sufni.App.Views.Controls;
using Sufni.Telemetry;

namespace Sufni.App.Tests.ViewModels.Editors;

public class SessionDetailViewModelTests
{
    private readonly SessionCoordinator sessionCoordinator = TestCoordinatorSubstitutes.Session();
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly IRecordedSessionGraph recordedSessionGraph = Substitute.For<IRecordedSessionGraph>();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly ISessionAnalysisService sessionAnalysisService = Substitute.For<ISessionAnalysisService>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    public SessionDetailViewModelTests()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        sessionPresentationService.CalculateDamperPercentages(
                Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(SessionDamperPercentages.Empty);
        sessionAnalysisService.Analyze(Arg.Any<SessionAnalysisRequest>()).Returns(SessionAnalysisResult.Hidden);
    }

    private SessionDetailViewModel CreateEditor(
        SessionSnapshot snapshot,
        IObservable<RecordedSessionDomainSnapshot>? watch = null,
        bool? isDesktop = null,
        ISessionPreferences? sessionPreferences = null,
        BikeCoordinator? bikeCoordinator = null)
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
            preferencesService,
            new InlineUiThreadDispatcher(),
            bikeCoordinator);
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
        Assert.Equal(BalanceSpeedMode.Both, editor.SelectedBalanceSpeedMode);
        Assert.Equal(VelocityAverageMode.SampleAveraged, editor.SelectedVelocityAverageMode);
        Assert.Equal(SessionAnalysisTargetProfile.Trail, editor.SelectedSessionAnalysisTargetProfile);
    }

    [AvaloniaFact]
    public void Construction_InitializesAirtimeHeaderAction()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));

        var action = Assert.Single(editor.TravelHeaderActions);
        Assert.True(editor.ShowAirtime);
        Assert.Equal("travel_airtime", action.Id);
        Assert.Equal(TelemetryPlotRowActionKind.Toggle, action.Kind);
        Assert.True(action.IsChecked);
        Assert.Equal("Hide airtime", action.ToolTip);
        Assert.NotNull(action.Command);

        AssertDefaultHiddenAirtimeAction(editor.VelocityHeaderActions, editor.ShowVelocityAirtime, "velocity_airtime");
        AssertDefaultHiddenAirtimeAction(editor.ImuHeaderActions, editor.ShowImuAirtime, "imu_airtime");
        AssertDefaultHiddenAirtimeAction(editor.PitchRollHeaderActions, editor.ShowPitchRollAirtime, "pitch_roll_airtime");
        AssertDefaultHiddenAirtimeAction(editor.SpeedHeaderActions, editor.ShowSpeedAirtime, "speed_airtime");
        AssertDefaultHiddenAirtimeAction(editor.ElevationHeaderActions, editor.ShowElevationAirtime, "elevation_airtime");
    }

    [AvaloniaFact]
    public void SessionAnalysisContextText_UsesDisplayNamesAndInvariantRangeFormatting()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
            editor.TelemetryData = TestTelemetryData.CreateProcessed();
            editor.SelectedTravelHistogramMode = TravelHistogramMode.DynamicSag;
            editor.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;
            editor.SelectedBalanceDisplacementMode = BalanceDisplacementMode.Travel;

            editor.SetAnalysisRange(0.02, 0.16);

            Assert.Equal("Selected range 0.0-0.2s", editor.SessionAnalysisRangeText);
            Assert.Equal("Travel: Dynamic sag  Velocity: Stroke-peak average  Balance: Travel / Both", editor.SessionAnalysisModesText);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [AvaloniaFact]
    public void TelemetryDataChanged_UpdatesNotesTemperatureAverages()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var telemetry = TestTelemetryData.CreateMinimal();
        telemetry.TemperatureAverages =
        [
            new TemperatureAverage(1, 21.26),
            new TemperatureAverage(2, 24.76)
        ];

        editor.TelemetryData = telemetry;

        Assert.True(editor.NotesPage.HasTemperatureAverages);
        Assert.Equal(2, editor.NotesPage.TemperatureAverages.Count);
        Assert.Equal("Fork", editor.NotesPage.TemperatureAverages[0].SensorName);
        Assert.Equal($"{21.26.ToString("F1", CultureInfo.CurrentCulture)} C", editor.NotesPage.TemperatureAverages[0].TemperatureText);
        Assert.Equal("Rear", editor.NotesPage.TemperatureAverages[1].SensorName);
        Assert.Equal($"{24.76.ToString("F1", CultureInfo.CurrentCulture)} C", editor.NotesPage.TemperatureAverages[1].TemperatureText);

        editor.TelemetryData = null;

        Assert.False(editor.NotesPage.HasTemperatureAverages);
        Assert.Empty(editor.NotesPage.TemperatureAverages);
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
        var telemetry = TestTelemetryData.CreateProcessed();
        var trackPoints = new List<TrackPoint> { new(1, 1, 1, 0) };
        var fullTrackPoints = new List<TrackPoint> { new(2, 2, 2, 0) };
        var result = new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            telemetry,
            Guid.NewGuid(),
            fullTrackPoints,
            trackPoints,
            400.0,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8),
            DampingSpeedCutoffs.Default,
            null));
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
    public async Task DampingSpeedCutoffPreview_RecomputesPercentagesAndAnalysisWithoutDirtying()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var initialCutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        var previewCutoffs = initialCutoffs.With(SuspensionType.Front, DampingSpeedCircuit.Compression, 260);
        var previewPercentages = new SessionDamperPercentages(11, 12, 13, 14, 15, 16, 17, 18);
        var owner = new DampingSpeedCutoffOwner(Guid.NewGuid(), 7);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry, initialCutoffs, owner));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);
        sessionPresentationService.ClearReceivedCalls();
        sessionAnalysisService.ClearReceivedCalls();
        sessionPresentationService
            .CalculateDamperPercentages(
                telemetry,
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Is<DampingSpeedCutoffs?>(value => value == previewCutoffs))
            .Returns(previewPercentages);

        editor.PreviewDampingSpeedCutoff(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);

        Assert.Equal(previewCutoffs, editor.DampingSpeedCutoffs);
        Assert.Equal(initialCutoffs, editor.PlotDampingSpeedCutoffs);
        Assert.Equal(previewPercentages, editor.DamperPercentages);
        Assert.Equal(11, editor.DamperPage.FrontHscPercentage);
        Assert.False(editor.IsDirty);
        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionAnalysisRequest>(request =>
            request.DampingSpeedCutoffs == previewCutoffs &&
            request.DamperPercentages == previewPercentages));
    }

    [AvaloniaFact]
    public async Task DampingSpeedCutoffCommit_PersistsThroughBikeCoordinatorWithoutDirtyingSession()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var bikeId = Guid.NewGuid();
        var owner = new DampingSpeedCutoffOwner(bikeId, 7);
        var initialCutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        var savedCutoffs = initialCutoffs.With(SuspensionType.Front, DampingSpeedCircuit.Rebound, 270);
        var savedBike = TestSnapshots.Bike(id: bikeId, updated: 8) with
        {
            FrontCompressionDampingCutoffMmPerSecond = savedCutoffs.Front.CompressionMmPerSecond,
            FrontReboundDampingCutoffMmPerSecond = savedCutoffs.Front.ReboundMmPerSecond,
            RearCompressionDampingCutoffMmPerSecond = savedCutoffs.Rear.CompressionMmPerSecond,
            RearReboundDampingCutoffMmPerSecond = savedCutoffs.Rear.ReboundMmPerSecond,
        };
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(
                bikeId,
                owner.BaselineUpdated,
                SuspensionType.Front,
                DampingSpeedCircuit.Rebound,
                270)
            .Returns(new BikeDampingSpeedCutoffUpdateResult.Saved(savedBike));
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry, initialCutoffs, owner));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, bikeCoordinator: bikeCoordinator);
        await editor.LoadedCommand.ExecuteAsync(null);

        await editor.CommitDampingSpeedCutoffAsync(SuspensionType.Front, DampingSpeedCircuit.Rebound, 266);

        await bikeCoordinator.Received(1).UpdateDampingSpeedCutoffAsync(
            bikeId,
            owner.BaselineUpdated,
            SuspensionType.Front,
            DampingSpeedCircuit.Rebound,
            270);
        Assert.Equal(savedCutoffs, editor.DampingSpeedCutoffs);
        Assert.Equal(savedCutoffs, editor.PlotDampingSpeedCutoffs);
        Assert.True(editor.CanEditDampingSpeedCutoffs);
        Assert.False(editor.IsDirty);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task DampingSpeedCutoffCommitFailure_RestoresPersistedCutoffAndReportsError()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var bikeCoordinator = TestCoordinatorSubstitutes.Bike();
        var bikeId = Guid.NewGuid();
        var owner = new DampingSpeedCutoffOwner(bikeId, 7);
        var initialCutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        bikeCoordinator.UpdateDampingSpeedCutoffAsync(
                bikeId,
                owner.BaselineUpdated,
                SuspensionType.Rear,
                DampingSpeedCircuit.Compression,
                500)
            .Returns(new BikeDampingSpeedCutoffUpdateResult.Failed("disk full"));
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry, initialCutoffs, owner));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, bikeCoordinator: bikeCoordinator);
        await editor.LoadedCommand.ExecuteAsync(null);
        editor.PreviewDampingSpeedCutoff(SuspensionType.Rear, DampingSpeedCircuit.Compression, 500);

        await editor.CommitDampingSpeedCutoffAsync(SuspensionType.Rear, DampingSpeedCircuit.Compression, 500);

        Assert.Equal(initialCutoffs, editor.DampingSpeedCutoffs);
        Assert.Equal(initialCutoffs, editor.PlotDampingSpeedCutoffs);
        Assert.False(editor.IsDirty);
        Assert.Contains(editor.ErrorMessages, message => message.Contains("disk full", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_AppliesPersistedPlotPreferences()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry();
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
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
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));
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
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
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
    public async Task TravelHeaderAction_TogglesAirtimeWithoutDirtyingOrPersistingPreferences()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(CreateVibrationTelemetry()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();

        var action = Assert.Single(editor.TravelHeaderActions);
        action.Command!.Execute(null);

        Assert.False(editor.ShowAirtime);
        Assert.False(action.IsChecked);
        Assert.Equal("Show airtime", action.ToolTip);
        Assert.False(editor.IsDirty);
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());

        var velocityAction = Assert.Single(editor.VelocityHeaderActions);
        velocityAction.Command!.Execute(null);

        Assert.True(editor.ShowVelocityAirtime);
        Assert.True(velocityAction.IsChecked);
        Assert.Equal("Hide airtime", velocityAction.ToolTip);
        Assert.False(editor.IsDirty);
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
    }

    [AvaloniaFact]
    public async Task GraphPreferenceChange_PersistsWithoutDirtyingSession()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        Func<SessionPreferences, SessionPreferences>? update = null;
        preferences.UpdateRecordedAsync(
                snapshot.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(CreateVibrationTelemetry()));
        SetDesktop(true);
        var graph = new SessionGraphPreferences(
        [
            new SessionGraphRowPreferences(TelemetryGraphRowIds.Imu, isExpanded: false),
            new SessionGraphRowPreferences(
                TelemetryGraphRowIds.Travel,
                children:
                [
                    new SessionGraphRowPreferences(TelemetryGraphRowIds.Velocity),
                ]),
        ]);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();

        editor.GraphPreferences = graph;

        Assert.False(editor.IsDirty);
        await preferences.Received(1).UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        Assert.NotNull(update);
        Assert.Equal(graph, update!(SessionPreferences.Default).Graph);
    }

    [AvaloniaFact]
    public async Task ProcessingPreferenceCommit_PersistsPreferenceAndRecomputesSession()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true, updated: 5);
        var recomputedSnapshot = snapshot with { Updated = 7 };
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        Func<SessionPreferences, SessionPreferences>? update = null;
        preferences.UpdateRecordedAsync(
                snapshot.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));
        sessionCoordinator.RecomputeAsync(snapshot.Id, snapshot.Updated, Arg.Any<CancellationToken>())
            .Returns(new SessionRecomputeResult.Recomputed(recomputedSnapshot.Updated));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();
        sessionStore.Get(snapshot.Id).Returns(recomputedSnapshot);

        editor.PreferencesPage.VelocityFilterWindowMilliseconds = 250;
        editor.PreferencesPage.CommitProcessingPreferenceChange();

        await WaitForAsync(() => editor.BaselineUpdated == recomputedSnapshot.Updated);
        await preferences.Received(1).UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        Assert.NotNull(update);
        Assert.Equal(250, update!(SessionPreferences.Default).Processing.VelocityFilterWindowMilliseconds);
        await sessionCoordinator.Received(1).RecomputeAsync(snapshot.Id, snapshot.Updated, Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_AppliesPersistedStatisticsWithoutSavingDuringHydration()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(
            preferences,
            snapshot.Id,
            new SessionPreferences(
                new SessionPlotPreferences(),
                new SessionStatisticsPreferences(
                    TravelHistogramMode.DynamicSag,
                    VelocityAverageMode.StrokePeakAveraged,
                    BalanceDisplacementMode.Travel,
                    BalanceSpeedMode.HighSpeed,
                    SessionAnalysisTargetProfile.DH)));
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));
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
    public async Task SyncedPreferenceArrival_AppliesWithoutRePersisting()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>();
        var syncStream = new Subject<SessionPreferences>();
        preferences.ObserveRecorded(snapshot.Id).Returns(syncStream);
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();

        var synced = SessionPreferences.Default with
        {
            Statistics = SessionPreferences.Default.Statistics with
            {
                TravelHistogramMode = TravelHistogramMode.DynamicSag,
            },
        };
        syncStream.OnNext(synced);

        Assert.Equal(TravelHistogramMode.DynamicSag, editor.SelectedTravelHistogramMode);
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
    }

    [AvaloniaFact]
    public async Task StatisticsPreferenceChange_RecomputesAnalysisAndPersistsAfterHydration()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var strokePeakPercentages = new SessionDamperPercentages(11, 21, 31, 41, 51, 61, 71, 81);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        Func<SessionPreferences, SessionPreferences>? update = null;
        preferences.UpdateRecordedAsync(
                snapshot.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        sessionPresentationService.CalculateDamperPercentages(
                telemetry,
                Arg.Is<TelemetryTimeRange?>(range => !range.HasValue),
                VelocityAverageMode.StrokePeakAveraged,
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(strokePeakPercentages);
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();
        sessionAnalysisService.ClearReceivedCalls();

        editor.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;

        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionAnalysisRequest>(request =>
            request.VelocityAverageMode == VelocityAverageMode.StrokePeakAveraged &&
            request.DamperPercentages == strokePeakPercentages));
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
        var telemetry = TestTelemetryData.CreateProcessed();
        var damperPercentages = new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var analysis = CreateAnalysisResult();
        var result = new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            telemetry,
            FullTrackId: null,
            FullTrackPoints: null,
            TrackPoints: null,
            MapVideoWidth: null,
            DamperPercentages: damperPercentages,
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            DampingSpeedCutoffOwner: null));
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
                    range.Value.EndSeconds == 0.16),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
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
            .CalculateDamperPercentages(
                telemetry,
                Arg.Is<TelemetryTimeRange?>(range => range.HasValue),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
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
            .CalculateDamperPercentages(
                telemetry,
                Arg.Is<TelemetryTimeRange?>(range => range.HasValue),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(rangePercentages);
        sessionPresentationService
            .CalculateDamperPercentages(
                telemetry,
                Arg.Is<TelemetryTimeRange?>(range => !range.HasValue),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
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
    public void ClearAnalysisRange_ClearsPendingAnalysisRangeBoundary()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var editor = CreateEditor(snapshot);
        editor.TelemetryData = CreateVibrationTelemetry();

        editor.SetAnalysisRangeBoundary(0.02);
        editor.ClearAnalysisRange();
        editor.SetAnalysisRangeBoundary(0.16);

        Assert.Null(editor.AnalysisRange);
    }

    [AvaloniaFact]
    public void DamperPercentagesChange_DoesNotIndependentlyRecomputeAnalysis()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        editor.TelemetryData = TestTelemetryData.CreateProcessed();
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
    public async Task Loaded_OnDesktop_WithImuAndNoStrokes_ShowsNoDataStates()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = CreateVibrationTelemetry(frontStrokes: false, rearStrokes: false);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(SurfaceStateKind.NoData, editor.FrontStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.NoData, editor.RearStatisticsState.Kind);
        Assert.Equal(SurfaceIndicatorKind.None, editor.FrontStatisticsState.Indicator);
        Assert.Equal("Not enough travel movement to calculate statistics.", editor.FrontStatisticsState.Message);
        Assert.Equal(SurfaceStateKind.NoData, editor.FrontForkVibrationState.Kind);
        Assert.Equal(SurfaceStateKind.NoData, editor.FrontFrameVibrationState.Kind);
        Assert.Equal(SurfaceStateKind.NoData, editor.RearForkVibrationState.Kind);
        Assert.Equal(SurfaceStateKind.NoData, editor.RearFrameVibrationState.Kind);
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
            DampingSpeedCutoffs.Default,
            false),
            TestTelemetryData.CreateProcessed(),
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
    public async Task Loaded_OnMobile_FromCacheWithNoStrokes_ShowsNoDataStatistics()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry(
            frontStrokes: false,
            rearStrokes: false,
            forkImu: false,
            frameImu: false);
        var result = new SessionMobileLoadResult.LoadedFromCache(new SessionCachePresentationData(
            null,
            null,
            null,
            null,
            null,
            null,
            SessionDamperPercentages.Empty,
            DampingSpeedCutoffs.Default,
            false),
            telemetry,
            null);
        sessionCoordinator.LoadMobileDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        SetDesktop(false);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));

        Assert.Equal(SurfaceStateKind.NoData, editor.FrontStatisticsState.Kind);
        Assert.Equal(SurfaceStateKind.NoData, editor.RearStatisticsState.Kind);
        Assert.Equal(SurfaceIndicatorKind.None, editor.FrontStatisticsState.Indicator);
        Assert.Equal("Not enough travel movement to calculate statistics.", editor.FrontStatisticsState.Message);
    }

    [AvaloniaFact]
    public async Task Loaded_OnMobile_FromCacheWithoutTelemetry_HidesExtendedStatistics()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var result = new SessionMobileLoadResult.LoadedFromCache(new SessionCachePresentationData(
            "front-travel",
            "rear-travel",
            "front-velocity",
            "rear-velocity",
            null,
            null,
            new SessionDamperPercentages(1, null, 2, null, 3, null, 4, null),
            DampingSpeedCutoffs.Default,
            false),
            null,
            null);
        sessionCoordinator.LoadMobileDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        SetDesktop(false);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));
        var springPage = editor.Pages.OfType<SpringPageViewModel>().Single();
        var damperPage = editor.Pages.OfType<DamperPageViewModel>().Single();

        Assert.True(springPage.FrontHistogramState.IsReady);
        Assert.True(damperPage.FrontHistogramState.IsReady);
        Assert.True(editor.FrontStatisticsState.IsHidden);
        Assert.True(editor.RearStatisticsState.IsHidden);
        Assert.True(editor.SessionAnalysis.State.IsHidden);
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
            DampingSpeedCutoffs.Default,
            false),
            TestTelemetryData.CreateProcessed(),
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
        var telemetry = TestTelemetryData.CreateProcessed();
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
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8),
            DampingSpeedCutoffs.Default,
            null)));

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
            DampingSpeedCutoffs.Default,
            false),
            TestTelemetryData.CreateProcessed(),
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
            DampingSpeedCutoffs.Default,
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
        var firstTelemetry = TestTelemetryData.CreateProcessed();
        var secondTelemetry = TestTelemetryData.CreateProcessed();
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
                        new SessionDamperPercentages(10, 20, 30, 40, 50, 60, 70, 80),
                        DampingSpeedCutoffs.Default,
                        null)));
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
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8),
            DampingSpeedCutoffs.Default,
            null)));

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
        var finalTelemetry = TestTelemetryData.CreateProcessed();
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
            TestTelemetryData.CreateProcessed(),
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(9, 9, 9, 9, 9, 9, 9, 9),
            DampingSpeedCutoffs.Default,
            null)));

        await finalLoadStarted.Task;
        finalPending.SetResult(new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            finalTelemetry,
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8),
            DampingSpeedCutoffs.Default,
            null)));

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
                Arg.Any<string>(),
                Arg.Any<string>())
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
                Arg.Any<string>(),
                Arg.Any<string>())
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
            Arg.Any<string>(),
            Arg.Any<string>());
        await sessionCoordinator.Received(1).RecomputeAsync(snapshot.Id, snapshot.Updated, Arg.Any<CancellationToken>());
        Assert.Equal(recomputedSnapshot.Updated, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task RecomputeSuccess_ClearsOldPresentationBeforeApplyingFreshTelemetry()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", hasProcessedData: true, updated: 5);
        var recomputedSnapshot = snapshot with { Updated = 7 };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var oldTelemetry = TestTelemetryData.CreateProcessed();
        var freshTelemetry = TestTelemetryData.CreateProcessed();
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
                Arg.Any<string>(),
                Arg.Any<string>())
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
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(false);

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.Initial,
            new SessionStaleness.DependencyHashChanged()));
        await Task.Yield();

        await dialogService.Received(1).ShowConfirmationAsync(
            Arg.Any<string>(),
            Arg.Any<string>());

        await editor.CloseCommand.ExecuteAsync(null);
        shell.Received(1).Close(editor);

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.DependencyChanged,
            new SessionStaleness.DependencyHashChanged()));
        await Task.Yield();

        await dialogService.Received(1).ShowConfirmationAsync(
            Arg.Any<string>(),
            Arg.Any<string>());
        await sessionCoordinator.DidNotReceive().RecomputeAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    private static void AssertDefaultHiddenAirtimeAction(
        IReadOnlyList<TelemetryPlotRowAction> actions,
        bool isVisible,
        string expectedId)
    {
        var action = Assert.Single(actions);
        Assert.False(isVisible);
        Assert.Equal(expectedId, action.Id);
        Assert.Equal(TelemetryPlotRowActionKind.Toggle, action.Kind);
        Assert.False(action.IsChecked);
        Assert.Equal("Show airtime", action.ToolTip);
        Assert.NotNull(action.Command);
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
                Arg.Any<string>(),
                Arg.Any<string>())
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
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

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

    [AvaloniaFact]
    public async Task RuntimeDerivedChange_DefersRecomputePrompt_UntilDesktopTabIsActive()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", hasProcessedData: true, updated: 5);
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var promptShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(new SessionDesktopLoadResult.TelemetryPending());
        dialogService.ShowConfirmationAsync(
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(_ =>
            {
                promptShown.TrySetResult();
                return Task.FromResult(false);
            });

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        editor.SetTabActive(true);
        await editor.LoadedCommand.ExecuteAsync(null);
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));

        editor.SetTabActive(false);
        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.DependencyChanged,
            new SessionStaleness.DependencyHashChanged()));
        await Task.Yield();

        await dialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());

        editor.SetTabActive(true);
        await promptShown.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await dialogService.Received(1).ShowConfirmationAsync(
            Arg.Any<string>(),
            Arg.Any<string>());
        await sessionCoordinator.DidNotReceive().RecomputeAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task RuntimeFreshUpdate_ReloadsPresentation_WhenUpdatedSnapshotArrives()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", hasProcessedData: true, updated: 5);
        var updatedSnapshot = snapshot with { Updated = 8 };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var oldTelemetry = TestTelemetryData.CreateProcessed();
        var freshTelemetry = TestTelemetryData.CreateProcessed();
        var loadCount = 0;

        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                loadCount++;
                return loadCount == 1
                    ? LoadedDesktopResult(oldTelemetry)
                    : LoadedDesktopResult(freshTelemetry);
            });

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        sessionStore.Get(snapshot.Id).Returns(snapshot, updatedSnapshot);

        await editor.LoadedCommand.ExecuteAsync(null);
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        Assert.Same(oldTelemetry, editor.TelemetryData);

        watch.OnNext(DomainFromSnapshot(updatedSnapshot));

        await WaitForAsync(() => ReferenceEquals(editor.TelemetryData, freshTelemetry));

        Assert.Equal(updatedSnapshot.Updated, editor.BaselineUpdated);
        await sessionCoordinator.Received(2).LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>());
        await dialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task RuntimeFreshUpdate_DeclinedDirtyReload_KeepsDraftAndCurrentPresentation()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", description: "persisted", hasProcessedData: true, updated: 5);
        var updatedSnapshot = snapshot with { Updated = 8, Description = "remote" };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var oldTelemetry = TestTelemetryData.CreateProcessed();

        sessionCoordinator.LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(oldTelemetry));
        dialogService.ShowConfirmationAsync(
                Arg.Any<string>(),
                Arg.Any<string>())
            .Returns(false);

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        editor.DescriptionText = "dirty draft";
        Assert.True(editor.IsDirty);

        watch.OnNext(DomainFromSnapshot(updatedSnapshot));
        await Task.Yield();

        Assert.True(editor.IsDirty);
        Assert.Equal("dirty draft", editor.DescriptionText);
        Assert.Equal(snapshot.Updated, editor.BaselineUpdated);
        Assert.Same(oldTelemetry, editor.TelemetryData);
        await sessionCoordinator.Received(1).LoadDesktopDetailAsync(snapshot.Id, Arg.Any<CancellationToken>());
        await dialogService.Received(1).ShowConfirmationAsync(
            Arg.Any<string>(),
            Arg.Any<string>());
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

    private static SessionDesktopLoadResult LoadedDesktopResult(
        TelemetryData telemetry,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null,
        DampingSpeedCutoffOwner? dampingSpeedCutoffOwner = null)
    {
        return new SessionDesktopLoadResult.Loaded(new SessionTelemetryPresentationData(
            telemetry,
            null,
            null,
            null,
            null,
            new SessionDamperPercentages(1, 2, 3, 4, 5, 6, 7, 8),
            dampingSpeedCutoffs ?? DampingSpeedCutoffs.Default,
            dampingSpeedCutoffOwner));
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
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
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

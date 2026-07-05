using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Globalization;
using System.ComponentModel;
using Avalonia;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Runtime.Presentation;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Sessions.Insights.ViewModels.SessionPages;
using Sufni.App.Sessions.Signals.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Extensions;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

[Collection("Ui")]
public class SessionDetailViewModelTests
{
    private readonly ISessionCoordinator sessionCoordinator = TestCoordinatorSubstitutes.Session();
    private readonly ITrackCoordinator trackCoordinator = TestCoordinatorSubstitutes.Track();
    private readonly ISessionStore sessionStore = Substitute.For<ISessionStore>();
    private readonly IRecordedSessionProjection recordedSessionProjection = Substitute.For<IRecordedSessionProjection>();
    private readonly ISessionPresentationService sessionPresentationService = Substitute.For<ISessionPresentationService>();
    private readonly ISessionInsightsService sessionAnalysisService = Substitute.For<ISessionInsightsService>();
    private readonly ITileLayerService tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    public SessionDetailViewModelTests()
    {
        tileLayerService.AvailableLayers.Returns([]);
        tileLayerService.InitializeAsync().Returns(Task.CompletedTask);
        sessionPresentationService.CalculateDampingPercentages(
                Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(),
                Arg.Any<VelocityAverageMode>(),
                Arg.Any<DampingSpeedCutoffs?>())
            .Returns(SessionDampingPercentages.Empty);
        sessionAnalysisService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(SessionInsightsResult.Hidden);
    }

    private SessionDetailViewModel CreateEditor(
        SessionSnapshot snapshot,
        IObservable<RecordedSessionDomainSnapshot>? watch = null,
        bool? isDesktop = null,
        ISessionPreferences? sessionPreferences = null,
        IBikeCoordinator? bikeCoordinator = null,
        IReadOnlyList<IRecordedSessionExtensionFactory>? recordedSessionExtensionFactories = null,
        IUiThreadDispatcher? uiThreadDispatcher = null,
        bool deferDomainHandlingWhenInactive = true,
        IRecordedSessionProcessingOptionCache? processingOptionCache = null,
        TestSessionProcessedTelemetryReader? processedTelemetryReader = null,
        IRecordedSessionDerivationWindowCache? derivationWindowCache = null,
        IEditorFactory? editorFactory = null)
    {
        if (isDesktop.HasValue)
        {
            TestApp.SetIsDesktop(isDesktop.Value);
        }

        recordedSessionProjection.WatchSession(snapshot.Id).Returns(watch ?? Observable.Empty<RecordedSessionDomainSnapshot>());
        sessionStore.Get(snapshot.Id).Returns(snapshot);
        var preferencesService = sessionPreferences ?? CreateSessionPreferences();
        var dispatcher = uiThreadDispatcher ?? new InlineUiThreadDispatcher();
        var analysisResultStateFactory = new RecordedSessionAnalysisResultStateFactory(
            new RecordedSessionAnalysisComputer(sessionAnalysisService),
            new InlineBackgroundTaskRunner(),
            dispatcher);
        return new SessionDetailViewModel(
            snapshot,
            sessionCoordinator,
            trackCoordinator,
            sessionStore,
            recordedSessionProjection,
            new TestMapViewModelFactory(tileLayerService),
            shell,
            dialogService,
            preferencesService,
            dispatcher,
            deferDomainHandlingWhenInactive,
            processingOptionCache ?? new InMemoryRecordedSessionProcessingOptionCache(),
            processedTelemetryReader ?? new TestSessionProcessedTelemetryReader(),
            analysisResultStateFactory,
            derivationWindowCache ?? Substitute.For<IRecordedSessionDerivationWindowCache>(),
            () => editorFactory ?? Substitute.For<IEditorFactory>(),
            bikeCoordinator,
            new ExtensionHostDependencies(
                recordedSessionExtensionFactories ?? [],
                Substitute.For<IExtensionDatabaseConnection>(),
                Substitute.For<IRecordedSessionDataReader>(),
                new InlineBackgroundTaskRunner()));
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

        Assert.Equal(SurfaceStateKind.Loading, editor.SignalsWorkspace.TravelSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.SignalsWorkspace.VelocitySignalState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.SignalsWorkspace.ImuSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.AnalysisWorkspace.FrontAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.AnalysisWorkspace.RearAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.AnalysisWorkspace.CompressionBalanceState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, editor.AnalysisWorkspace.ReboundBalanceState.Kind);
        Assert.True(editor.AnalysisWorkspace.FrontForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearFrameVibrationState.IsHidden);
        Assert.Equal(SurfaceStateKind.Loading, editor.MediaWorkspace.MapState.Kind);
        Assert.True(editor.ScreenState.IsReady);
    }

    [AvaloniaFact]
    public void Construction_ExposesProjectedMobileWorkspace()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);

        var editor = CreateEditor(snapshot);

        Assert.Same(editor.Pages, editor.MobileWorkspace.Pages);
        Assert.Same(editor.Timeline, editor.SignalsWorkspace.Timeline);
        Assert.Same(editor.ExtensionSlots, editor.SignalsWorkspace.ExtensionSlots);
        Assert.Same(editor.Timeline, editor.MediaWorkspace.Timeline);
        Assert.Same(editor.ExtensionSlots, editor.MediaWorkspace.ExtensionSlots);
        Assert.Same(editor.ExtensionSlots, editor.AnalysisWorkspace.ExtensionSlots);
        Assert.Same(editor.NotesPage, editor.SidebarWorkspace.NotesPage);
        Assert.Same(editor.PreferencesPage, editor.SidebarWorkspace.PreferencesPage);
        Assert.Same(editor.SaveCommand, editor.SidebarWorkspace.SaveCommand);
        Assert.Same(editor.ResetCommand, editor.SidebarWorkspace.ResetCommand);
        var signalsPage = Assert.IsType<RecordedSignalsPageViewModel>(editor.Pages[0]);
        Assert.Same(editor.SignalsWorkspace, signalsPage.Workspace);
        Assert.Same(editor.AnalysisWorkspace, editor.Pages.OfType<SpringPageViewModel>().Single().AnalysisWorkspace);
        Assert.Same(editor.AnalysisWorkspace, editor.Pages.OfType<StrokesPageViewModel>().Single().Workspace);
        Assert.Same(editor.AnalysisWorkspace, editor.DampingPage.AnalysisWorkspace);
        Assert.Same(editor.AnalysisWorkspace, editor.Pages.OfType<BalancePageViewModel>().Single().AnalysisWorkspace);
        Assert.Same(editor.AnalysisWorkspace, editor.Pages.OfType<VibrationPageViewModel>().Single().Workspace);
        Assert.Same(editor.AnalysisWorkspace, editor.Pages.OfType<SessionInsightsPageViewModel>().Single().Workspace);
        Assert.Equal(snapshot, editor.CurrentSessionSnapshot);
        Assert.Equal(editor.ScreenState, editor.MobileWorkspace.ScreenState);
        Assert.Equal(editor.SessionOperationState, editor.MobileWorkspace.SessionOperationState);
    }

    [AvaloniaFact]
    public void DerivedStateEmissions_DoNotReplaceStableRuntimeObjects()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var pages = editor.Pages;
        var timeline = editor.Timeline;
        var sourceVisibility = editor.SourceVisibility;
        var extensionSlots = editor.ExtensionSlots;
        var mapViewModel = editor.MapViewModel;

        editor.SetTelemetryData(TestTelemetryData.CreateProcessed());
        editor.SetAnalysisRange(0.02, 0.16);
        editor.SetMapState(SurfacePresentationState.Ready);
        editor.SetMediaUrl("session-media.mp4");
        editor.SetRecordedSignalStates(
            SurfacePresentationState.Ready,
            SurfacePresentationState.Ready,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);
        editor.SetRecordedAnalysisStates(new RecordedAnalysisPresentationState(
            SurfacePresentationState.Ready,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden));
        editor.SetSessionOperationState(SessionOperationPresentationState.Progress("working", 50));

        Assert.Same(pages, editor.Pages);
        Assert.Same(pages, editor.MobileWorkspace.Pages);
        Assert.Same(timeline, editor.Timeline);
        Assert.Same(timeline, editor.SignalsWorkspace.Timeline);
        Assert.Same(timeline, editor.MediaWorkspace.Timeline);
        Assert.Same(sourceVisibility, editor.SourceVisibility);
        Assert.Same(sourceVisibility, editor.SignalsWorkspace.SourceVisibility);
        Assert.Same(extensionSlots, editor.ExtensionSlots);
        Assert.Same(extensionSlots, editor.SignalsWorkspace.ExtensionSlots);
        Assert.Same(extensionSlots, editor.MediaWorkspace.ExtensionSlots);
        Assert.Same(extensionSlots, editor.AnalysisWorkspace.ExtensionSlots);
        Assert.Same(mapViewModel, editor.MapViewModel);
        Assert.Same(mapViewModel, editor.MediaWorkspace.MapViewModel);
    }

    [AvaloniaFact]
    public async Task MobileWorkspace_TracksOwnerPresentationState()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(new SessionDetailLoadResult.Failed("boom"));
        var editor = CreateEditor(snapshot);
        var observed = new List<string?>();
        ((INotifyPropertyChanged)editor.MobileWorkspace).PropertyChanged += (_, args) =>
            observed.Add(args.PropertyName);

        await editor.LoadedCommand.ExecuteAsync(null);
        editor.SetSessionOperationState(SessionOperationPresentationState.Progress("working", 25));

        Assert.Equal(editor.ScreenState, editor.MobileWorkspace.ScreenState);
        Assert.Equal(editor.SessionOperationState, editor.MobileWorkspace.SessionOperationState);
        Assert.Contains(nameof(ISessionShellMobileWorkspace.ScreenState), observed);
        Assert.Contains(nameof(ISessionShellMobileWorkspace.SessionOperationState), observed);
    }

    [AvaloniaFact]
    public void SharedSessionState_UpdatesOwnerAndWorkspaceState()
    {
        var editor = CreateEditor(TestSnapshots.Session());
        var telemetry = TestTelemetryData.CreateProcessed();

        editor.SetTelemetryData(telemetry);
        editor.SetAnalysisRange(0.02, 0.16);

        Assert.Same(telemetry, editor.CurrentTelemetryData);
        Assert.Same(telemetry, editor.SignalsWorkspace.TelemetryData);
        Assert.Equal(new TelemetryTimeRange(0.02, 0.16), editor.CurrentAnalysisRange);
        Assert.Equal(editor.CurrentAnalysisRange, editor.SignalsWorkspace.AnalysisRange);
    }

    [AvaloniaFact]
    public void MediaWorkspace_TracksOwnerMediaState()
    {
        var editor = CreateEditor(TestSnapshots.Session());
        var mapState = SurfacePresentationState.Ready;
        const double mediaColumnWidth = 480;
        const string mediaUrl = "session-media.mp4";

        editor.SetMapState(mapState);
        editor.SetMediaColumnWidth(mediaColumnWidth);
        editor.SetMediaUrl(mediaUrl);

        Assert.Same(editor.MapViewModel, editor.MediaWorkspace.MapViewModel);
        Assert.Equal(mapState, editor.MediaWorkspace.MapState);
        Assert.Equal(SurfacePresentationState.Ready, editor.MediaWorkspace.MediaPaneState);
        Assert.Equal(mediaColumnWidth, editor.MediaWorkspace.MediaColumnWidth);
        Assert.Equal(mediaUrl, editor.MediaWorkspace.MediaUrl);
        Assert.True(editor.MediaWorkspace.HasMediaContent);
    }

    [AvaloniaFact]
    public void SignalsWorkspace_TracksContextSignalStateAndCommands()
    {
        var editor = CreateEditor(TestSnapshots.Session());
        var telemetry = TestTelemetryData.CreateProcessed();
        var signalLayoutPreferences = SignalLayoutPreferences.Default with
        {
            Rows = [new SignalLayoutRowPreferences(SignalRowIds.Velocity, true, [])],
        };

        editor.SetTelemetryData(telemetry);
        editor.SetRecordedSignalStates(
            SurfacePresentationState.Ready,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);
        var velocityAirtimeAction = GetRowAction(editor.VelocityHeaderActions, "velocity_airtime");
        velocityAirtimeAction.Command!.Execute(null);
        var selection = CreateFrontDampingSelection(telemetry, editor.AnalysisWorkspace.SelectedVelocityAverageMode);
        editor.SelectAnalysisRangeCommand.Execute(selection);
        editor.SignalsWorkspace.SetAnalysisRange(1, 2);
        editor.SignalsWorkspace.SignalLayoutPreferences = signalLayoutPreferences;

        Assert.Same(editor.CurrentTelemetryData, editor.SignalsWorkspace.TelemetryData);
        Assert.Equal(editor.CurrentAnalysisRange, editor.SignalsWorkspace.AnalysisRange);
        Assert.Equal(SurfacePresentationState.Ready, editor.SignalsWorkspace.TravelSignalState);
        Assert.True(editor.SignalsWorkspace.ShowVelocityAirtime);
        Assert.NotEmpty(editor.SignalsWorkspace.AnalysisSelectionHighlightRanges);
        Assert.All(editor.SignalsWorkspace.AnalysisSelectionHighlightRanges, range => Assert.Equal(SuspensionType.Front, range.SuspensionType));
        Assert.True(editor.SignalsWorkspace.HasAnalysisSelection);
        Assert.Equal(signalLayoutPreferences, editor.SignalLayoutPreferences);
        Assert.Equal(signalLayoutPreferences, editor.SignalsWorkspace.SignalLayoutPreferences);
    }

    [AvaloniaFact]
    public void AnalysisWorkspace_TracksContextAnalysisStateAndCommands()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var telemetry = TestTelemetryData.CreateProcessed();
        var observed = new List<string?>();
        ((INotifyPropertyChanged)editor.AnalysisWorkspace).PropertyChanged += (_, args) =>
            observed.Add(args.PropertyName);

        editor.SetTelemetryData(telemetry);
        editor.SetAnalysisRange(0.02, 0.16);
        editor.SetRecordedAnalysisStates(new RecordedAnalysisPresentationState(
            SurfacePresentationState.Ready,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden));
        editor.AnalysisWorkspace.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;
        var selection = CreateFrontDampingSelection(telemetry, editor.AnalysisWorkspace.SelectedVelocityAverageMode);

        editor.AnalysisWorkspace.SelectAnalysisRangeCommand.Execute(selection);

        Assert.Same(telemetry, editor.AnalysisWorkspace.TelemetryData);
        Assert.Equal(editor.CurrentAnalysisRange, editor.AnalysisWorkspace.AnalysisRange);
        Assert.Equal("Selected range 0.0-0.2s", editor.AnalysisWorkspace.SessionAnalysisRangeText);
        Assert.Equal(SurfacePresentationState.Ready, editor.AnalysisWorkspace.FrontAnalysisState);
        Assert.Equal(VelocityAverageMode.StrokePeakAveraged, editor.AnalysisWorkspace.SelectedVelocityAverageMode);
        Assert.Equal(VelocityAverageMode.StrokePeakAveraged, editor.AnalysisWorkspace.SelectedVelocityAverageMode);
        Assert.Equal(editor.SessionAnalysisModesText, editor.AnalysisWorkspace.SessionAnalysisModesText);
        Assert.Equal(selection, editor.ActiveFrontAnalysisSelection);
        Assert.Equal(selection, editor.AnalysisWorkspace.ActiveFrontAnalysisSelection);
        Assert.Contains(nameof(ISessionAnalysisWorkspace.TelemetryData), observed);
        Assert.Contains(nameof(ISessionAnalysisWorkspace.AnalysisRange), observed);
        Assert.Contains(nameof(ISessionAnalysisWorkspace.FrontAnalysisState), observed);
        Assert.Contains(nameof(ISessionAnalysisWorkspace.SelectedVelocityAverageMode), observed);
        Assert.Contains(nameof(ISessionAnalysisWorkspace.ActiveFrontAnalysisSelection), observed);
    }

    [AvaloniaFact]
    public void SidebarWorkspace_DelegatesEditableFieldsToShellAndNotesPage()
    {
        var editor = CreateEditor(TestSnapshots.Session(name: "Before", description: "old notes"));

        editor.SidebarWorkspace.Name = "After";
        editor.SidebarWorkspace.DescriptionText = "new notes";

        Assert.Equal("After", editor.Name);
        Assert.Equal("After", editor.SidebarWorkspace.Name);
        Assert.Equal("new notes", editor.NotesPage.Description);
        Assert.Equal("new notes", editor.SidebarWorkspace.DescriptionText);
        Assert.Same(editor.NotesPage.ForkSettings, editor.SidebarWorkspace.ForkSettings);
        Assert.Same(editor.NotesPage.ShockSettings, editor.SidebarWorkspace.ShockSettings);
    }

    [AvaloniaFact]
    public void Construction_InitializesAnalysisModeDefaultsAndOptions()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);

        var editor = CreateEditor(snapshot);

        Assert.Equal(TravelDistributionMode.ActiveSuspension, editor.AnalysisWorkspace.SelectedTravelDistributionMode);
        Assert.Equal(BalanceDisplacementMode.Zenith, editor.AnalysisWorkspace.SelectedBalanceDisplacementMode);
        Assert.Equal(BalanceSpeedMode.Both, editor.AnalysisWorkspace.SelectedBalanceSpeedMode);
        Assert.Equal(VelocityAverageMode.SampleAveraged, editor.AnalysisWorkspace.SelectedVelocityAverageMode);
        Assert.Equal(SessionInsightsTargetProfile.Trail, editor.AnalysisWorkspace.SelectedSessionInsightsTargetProfile);
        Assert.Same(editor.TravelDistributionModeOptions, editor.AnalysisWorkspace.TravelDistributionModeOptions);
        Assert.Same(editor.VelocityAverageModeOptions, editor.AnalysisWorkspace.VelocityAverageModeOptions);
        Assert.Same(editor.BalanceDisplacementModeOptions, editor.AnalysisWorkspace.BalanceDisplacementModeOptions);
        Assert.Same(editor.BalanceSpeedModeOptions, editor.AnalysisWorkspace.BalanceSpeedModeOptions);
        Assert.Same(editor.SessionInsightsTargetProfileOptions, editor.AnalysisWorkspace.SessionInsightsTargetProfileOptions);
    }

    [AvaloniaFact]
    public void Construction_InitializesAirtimeHeaderAction()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));

        var action = GetRowAction(editor.TravelHeaderActions, "travel_airtime");
        Assert.True(editor.SignalsWorkspace.ShowAirtime);
        Assert.Equal("travel_airtime", action.Id);
        Assert.Equal(SignalRowActionKind.Toggle, action.Kind);
        Assert.True(action.IsChecked);
        Assert.Equal("Hide airtime", action.ToolTip);
        Assert.NotNull(action.Command);

        AssertDefaultHiddenAirtimeAction(editor.VelocityHeaderActions, editor.SignalsWorkspace.ShowVelocityAirtime, "velocity_airtime");
        AssertDefaultHiddenAirtimeAction(editor.ImuHeaderActions, editor.SignalsWorkspace.ShowImuAirtime, "imu_airtime");
        AssertDefaultHiddenAirtimeAction(editor.PitchRollHeaderActions, editor.SignalsWorkspace.ShowPitchRollAirtime, "pitch_roll_airtime");
        AssertDefaultHiddenAirtimeAction(editor.SpeedHeaderActions, editor.SignalsWorkspace.ShowSpeedAirtime, "speed_airtime");
        AssertDefaultHiddenAirtimeAction(editor.ElevationHeaderActions, editor.SignalsWorkspace.ShowElevationAirtime, "elevation_airtime");

        AssertDefaultDisabledAnalysisSelectionAction(editor.TravelHeaderActions, editor.SignalsWorkspace.ShowAnalysisSelection, "travel_analysis_selection");
        AssertDefaultDisabledAnalysisSelectionAction(editor.VelocityHeaderActions, editor.SignalsWorkspace.ShowVelocityAnalysisSelection, "velocity_analysis_selection");
        AssertDefaultDisabledAnalysisSelectionAction(editor.ImuHeaderActions, editor.SignalsWorkspace.ShowImuAnalysisSelection, "imu_analysis_selection");
        AssertDefaultDisabledAnalysisSelectionAction(editor.PitchRollHeaderActions, editor.SignalsWorkspace.ShowPitchRollAnalysisSelection, "pitch_roll_analysis_selection");
        AssertDefaultDisabledAnalysisSelectionAction(editor.SpeedHeaderActions, editor.SignalsWorkspace.ShowSpeedAnalysisSelection, "speed_analysis_selection");
        AssertDefaultDisabledAnalysisSelectionAction(editor.ElevationHeaderActions, editor.SignalsWorkspace.ShowElevationAnalysisSelection, "elevation_analysis_selection");
    }

    [AvaloniaFact]
    public void SelectAnalysisRangeCommand_SelectsDampingRangeAndTogglesOffWhenRepeated()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var telemetry = TestTelemetryData.CreateProcessed();
        editor.SetTelemetryData(telemetry);
        var selection = CreateFrontDampingSelection(telemetry, editor.AnalysisWorkspace.SelectedVelocityAverageMode);

        editor.SelectAnalysisRangeCommand.Execute(selection);

        Assert.Equal(selection, editor.ActiveFrontAnalysisSelection);
        Assert.Null(editor.ActiveRearAnalysisSelection);
        Assert.True(editor.SignalsWorkspace.HasAnalysisSelection);
        Assert.NotEmpty(editor.SignalsWorkspace.AnalysisSelectionHighlightRanges);
        Assert.All(editor.SignalsWorkspace.AnalysisSelectionHighlightRanges, range => Assert.Equal(SuspensionType.Front, range.SuspensionType));
        Assert.True(editor.SignalsWorkspace.ShowAnalysisSelection);

        var travelSelectionAction = GetRowAction(editor.TravelHeaderActions, "travel_analysis_selection");
        Assert.True(travelSelectionAction.IsEnabled);
        Assert.True(travelSelectionAction.IsChecked);
        Assert.Equal("Hide analysis selection", travelSelectionAction.ToolTip);

        editor.SelectAnalysisRangeCommand.Execute(selection);

        Assert.Null(editor.ActiveFrontAnalysisSelection);
        Assert.False(editor.SignalsWorkspace.HasAnalysisSelection);
        Assert.Empty(editor.SignalsWorkspace.AnalysisSelectionHighlightRanges);
        Assert.False(editor.SignalsWorkspace.ShowAnalysisSelection);
        Assert.False(travelSelectionAction.IsEnabled);
    }

    [AvaloniaFact]
    public void Construction_InitializesPlotContextMenuAutozoomActions()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));

        var expectedRowIds = new[]
        {
            SignalRowIds.Travel,
            SignalRowIds.Velocity,
            SignalRowIds.Imu,
            SignalRowIds.PitchRoll,
            SignalRowIds.Speed,
            SignalRowIds.Elevation,
        };

        Assert.Equal(expectedRowIds.OrderBy(id => id), editor.SignalPlotContextMenuActionsBySignalRowId.Keys.OrderBy(id => id));
        foreach (var rowId in expectedRowIds)
        {
            var actions = editor.SignalPlotContextMenuActionsBySignalRowId[rowId];
            Assert.Contains(actions, action => action.Id == "zoom-selection" && action.Label == "Zoom selection");
            Assert.Contains(actions, action => action.Id == "analysis-range-set-start" && action.Label == "Set analysis start here");
            Assert.Contains(actions, action => action.Id == "analysis-range-set-end" && action.Label == "Set analysis end here");
            Assert.Contains(actions, action => action.Id == "analysis-range-clear" && action.Label == "Clear analysis range");
            Assert.Contains(actions, action => action.Id == "gps-mark-gps-event");
            Assert.Contains(actions, action => action.Id == "gps-mark-telemetry-event");

            Assert.All(actions, action => Assert.NotNull(action.Command));
        }
    }

    [AvaloniaFact]
    public void Construction_WithStateReplay_InitializesTimelineAlignmentCommands()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var gpsAction = GetPlotContextAction(editor, "gps-mark-gps-event");
        var telemetryAction = GetPlotContextAction(editor, "gps-mark-telemetry-event");
        var cancelAction = GetPlotContextAction(editor, "gps-cancel-alignment");
        var context = new TelemetryPlotContextMenuContext(
            SignalRowIds.Travel,
            ClickSeconds: 5,
            DurationSeconds: 10,
            AnalysisRange: null);

        Assert.NotNull(gpsAction.Command);
        Assert.NotNull(telemetryAction.Command);
        Assert.NotNull(cancelAction.Command);
        Assert.False(gpsAction.Command.CanExecute(context));
        Assert.False(telemetryAction.Command.CanExecute(context));
        Assert.False(cancelAction.Command.CanExecute(context));
    }

    [AvaloniaFact]
    public void AnalysisRangeContextMenuActions_SetStartEndAndClearRange()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        editor.SetTelemetryData(TestTelemetryData.CreateMinimal(duration: 10));
        var setStart = GetPlotContextAction(editor, "analysis-range-set-start");
        var setEnd = GetPlotContextAction(editor, "analysis-range-set-end");
        var clear = GetPlotContextAction(editor, "analysis-range-clear");
        var startContext = new TelemetryPlotContextMenuContext(SignalRowIds.Travel, 3, 10, null);
        var endContext = new TelemetryPlotContextMenuContext(SignalRowIds.Travel, 7, 10, null);

        Assert.True(setStart.Command.CanExecute(startContext));
        Assert.True(setEnd.Command.CanExecute(endContext));
        Assert.False(clear.Command.CanExecute(startContext));

        setStart.Command.Execute(startContext);
        Assert.Null(editor.CurrentAnalysisRange);

        setEnd.Command.Execute(endContext);
        Assert.Equal(3, editor.CurrentAnalysisRange?.StartSeconds);
        Assert.Equal(7, editor.CurrentAnalysisRange?.EndSeconds);

        setStart.Command.Execute(new TelemetryPlotContextMenuContext(
            SignalRowIds.Travel,
            ClickSeconds: 2,
            DurationSeconds: 10,
            AnalysisRange: editor.CurrentAnalysisRange));
        Assert.Equal(2, editor.CurrentAnalysisRange?.StartSeconds);
        Assert.Equal(7, editor.CurrentAnalysisRange?.EndSeconds);

        setEnd.Command.Execute(new TelemetryPlotContextMenuContext(
            SignalRowIds.Travel,
            ClickSeconds: 8,
            DurationSeconds: 10,
            AnalysisRange: editor.CurrentAnalysisRange));
        Assert.Equal(2, editor.CurrentAnalysisRange?.StartSeconds);
        Assert.Equal(8, editor.CurrentAnalysisRange?.EndSeconds);

        var clearContext = new TelemetryPlotContextMenuContext(
            SignalRowIds.Travel,
            ClickSeconds: 5,
            DurationSeconds: 10,
            AnalysisRange: editor.CurrentAnalysisRange);
        Assert.True(clear.Command.CanExecute(clearContext));
        clear.Command.Execute(clearContext);

        Assert.Null(editor.CurrentAnalysisRange);
        Assert.False(editor.IsDirty);
    }

    [AvaloniaFact]
    public void AnalysisRangeContextMenuActions_CannotExecuteWithoutTelemetryOrValidContext()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var setStart = GetPlotContextAction(editor, "analysis-range-set-start");
        var setEnd = GetPlotContextAction(editor, "analysis-range-set-end");
        var clear = GetPlotContextAction(editor, "analysis-range-clear");
        var context = new TelemetryPlotContextMenuContext(SignalRowIds.Travel, 3, 10, null);

        Assert.False(setStart.Command.CanExecute(context));
        Assert.False(setEnd.Command.CanExecute(context));
        Assert.False(clear.Command.CanExecute(context));

        editor.SetTelemetryData(TestTelemetryData.CreateMinimal(duration: 10));
        Assert.False(setStart.Command.CanExecute(null));
        Assert.False(setEnd.Command.CanExecute(new TelemetryPlotContextMenuContext("unknown", 3, 10, null)));
        Assert.False(clear.Command.CanExecute(new TelemetryPlotContextMenuContext(SignalRowIds.Travel, double.NaN, 10, null)));
    }

    [AvaloniaFact]
    public void AnalysisRangeContextMenuClear_ClearsPendingBoundary()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        editor.SetTelemetryData(TestTelemetryData.CreateMinimal(duration: 10));
        var setStart = GetPlotContextAction(editor, "analysis-range-set-start");
        var setEnd = GetPlotContextAction(editor, "analysis-range-set-end");
        var clear = GetPlotContextAction(editor, "analysis-range-clear");
        var startContext = new TelemetryPlotContextMenuContext(SignalRowIds.Travel, 3, 10, null);
        var endContext = new TelemetryPlotContextMenuContext(SignalRowIds.Travel, 7, 10, null);

        setStart.Command.Execute(startContext);
        Assert.True(clear.Command.CanExecute(startContext));

        clear.Command.Execute(startContext);
        setEnd.Command.Execute(endContext);

        Assert.Null(editor.CurrentAnalysisRange);
    }

    [AvaloniaFact]
    public async Task GpsContextMenuAlignment_PersistsOffsetThroughCoordinator()
    {
        var fullTrackId = Guid.NewGuid();
        var snapshot = TestSnapshots.Session(hasProcessedData: true, updated: 5) with
        {
            FullTrackId = fullTrackId,
            GpsOffsetSeconds = 1.0,
        };
        var telemetry = TestTelemetryData.CreateProcessed();
        var initialTrackPoints = new List<TrackPoint>
        {
            new(telemetry.Metadata.Timestamp + 1, 1, 1, 0, 10),
            new(telemetry.Metadata.Timestamp + 2, 2, 2, 0, 20),
        };
        // The GPS-offset write is one-way: the coordinator persists the offset and
        // upserts the store, returning success. The refreshed track/baseline reach
        // the editor through its watch reaction, not a pushed result.
        trackCoordinator.UpdateSessionGpsOffsetAsync(
                snapshot.Id,
                fullTrackId,
                telemetry,
                4.0,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var editor = CreateEditor(snapshot);
        editor.SetTelemetryData(telemetry);
        editor.SetTrackPoints(initialTrackPoints);
        var gpsEventContext = new TelemetryPlotContextMenuContext(
            SignalRowIds.Travel,
            ClickSeconds: 8.0,
            DurationSeconds: 20.0,
            AnalysisRange: null);
        var telemetryEventContext = new TelemetryPlotContextMenuContext(
            SignalRowIds.Travel,
            ClickSeconds: 5.0,
            DurationSeconds: 20.0,
            AnalysisRange: null);
        var gpsAction = editor.SignalPlotContextMenuActionsBySignalRowId[SignalRowIds.Travel]
            .Single(action => action.Id == "gps-mark-gps-event");
        var telemetryAction = editor.SignalPlotContextMenuActionsBySignalRowId[SignalRowIds.Travel]
            .Single(action => action.Id == "gps-mark-telemetry-event");
        var telemetryCommand = Assert.IsAssignableFrom<IAsyncRelayCommand<TelemetryPlotContextMenuContext?>>(telemetryAction.Command);

        Assert.True(gpsAction.Command.CanExecute(gpsEventContext));
        gpsAction.Command.Execute(gpsEventContext);

        Assert.False(gpsAction.Command.CanExecute(gpsEventContext));
        Assert.True(telemetryAction.Command.CanExecute(telemetryEventContext));

        await telemetryCommand.ExecuteAsync(telemetryEventContext);

        await trackCoordinator.Received(1).UpdateSessionGpsOffsetAsync(
            snapshot.Id,
            fullTrackId,
            telemetry,
            4.0,
            Arg.Any<CancellationToken>());
        Assert.False(editor.IsDirty);
    }

    [AvaloniaFact]
    public void AutozoomPlot_FullSession_WhenNoSelection()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var action = GetAutozoomAction(editor);

        action.Command.Execute(new TelemetryPlotContextMenuContext(
            SignalRowIds.Travel,
            ClickSeconds: 5,
            DurationSeconds: 10,
            AnalysisRange: null));

        Assert.Equal(0, editor.Timeline.VisibleRangeStart, 6);
        Assert.Equal(1, editor.Timeline.VisibleRangeEnd, 6);
        Assert.False(editor.IsDirty);
    }

    [AvaloniaFact]
    public void ZoomSelection_UsesSelectionRegardlessOfClickPosition()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var action = GetAutozoomAction(editor);

        action.Command.Execute(new TelemetryPlotContextMenuContext(
            SignalRowIds.Travel,
            ClickSeconds: 6,
            DurationSeconds: 10,
            AnalysisRange: new TelemetryTimeRange(2, 4)));

        Assert.Equal(0.19, editor.Timeline.VisibleRangeStart, 6);
        Assert.Equal(0.41, editor.Timeline.VisibleRangeEnd, 6);
    }

    [AvaloniaFact]
    public void AutozoomPlot_SelectedRangeWithPadding_WhenClickInsideSelection()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var action = GetAutozoomAction(editor);

        action.Command.Execute(new TelemetryPlotContextMenuContext(
            SignalRowIds.Travel,
            ClickSeconds: 3,
            DurationSeconds: 10,
            AnalysisRange: new TelemetryTimeRange(2, 4)));

        Assert.Equal(0.19, editor.Timeline.VisibleRangeStart, 6);
        Assert.Equal(0.41, editor.Timeline.VisibleRangeEnd, 6);
    }

    [AvaloniaFact]
    public void AutozoomPlot_SelectedRangeClampsAtSessionStart()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var action = GetAutozoomAction(editor);

        action.Command.Execute(new TelemetryPlotContextMenuContext(
            SignalRowIds.Travel,
            ClickSeconds: 0.5,
            DurationSeconds: 10,
            AnalysisRange: new TelemetryTimeRange(0, 1)));

        Assert.Equal(0, editor.Timeline.VisibleRangeStart, 6);
        Assert.Equal(0.105, editor.Timeline.VisibleRangeEnd, 6);
    }

    [AvaloniaFact]
    public void AutozoomPlot_SelectedRangeEnforcesMinimumSpan()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var action = GetAutozoomAction(editor);

        action.Command.Execute(new TelemetryPlotContextMenuContext(
            SignalRowIds.Travel,
            ClickSeconds: 50,
            DurationSeconds: 100,
            AnalysisRange: new TelemetryTimeRange(49.9, 50.1)));

        Assert.Equal(0.01, editor.Timeline.VisibleRangeEnd - editor.Timeline.VisibleRangeStart, 6);
        Assert.Equal(0.495, editor.Timeline.VisibleRangeStart, 6);
        Assert.Equal(0.505, editor.Timeline.VisibleRangeEnd, 6);
    }

    [AvaloniaFact]
    public void AutozoomPlot_CannotExecute_WhenContextOrDurationIsInvalid()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        var action = GetAutozoomAction(editor);

        Assert.False(action.Command.CanExecute(null));
        Assert.False(action.Command.CanExecute(new TelemetryPlotContextMenuContext(SignalRowIds.Travel, 1, double.NaN, null)));
        Assert.False(action.Command.CanExecute(new TelemetryPlotContextMenuContext(SignalRowIds.Travel, 1, double.PositiveInfinity, null)));
        Assert.False(action.Command.CanExecute(new TelemetryPlotContextMenuContext(SignalRowIds.Travel, 1, 0, null)));
        Assert.False(action.Command.CanExecute(new TelemetryPlotContextMenuContext(SignalRowIds.Travel, 1, -1, null)));
        Assert.False(action.Command.CanExecute(new TelemetryPlotContextMenuContext(SignalRowIds.Travel, double.NaN, 10, null)));
    }

    [AvaloniaFact]
    public void SessionInsightsContextText_UsesDisplayNamesAndInvariantRangeFormatting()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
        try
        {
            var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
            editor.SetTelemetryData(TestTelemetryData.CreateProcessed());
            editor.AnalysisWorkspace.SelectedTravelDistributionMode = TravelDistributionMode.DynamicSag;
            editor.AnalysisWorkspace.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;
            editor.AnalysisWorkspace.SelectedBalanceDisplacementMode = BalanceDisplacementMode.Travel;

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

        editor.SetTelemetryData(telemetry);

        Assert.True(editor.NotesPage.HasTemperatureAverages);
        Assert.Equal(2, editor.NotesPage.TemperatureAverages.Count);
        Assert.Equal("Fork", editor.NotesPage.TemperatureAverages[0].SensorName);
        Assert.Equal($"{21.26.ToString("F1", CultureInfo.CurrentCulture)} C", editor.NotesPage.TemperatureAverages[0].TemperatureText);
        Assert.Equal("Rear", editor.NotesPage.TemperatureAverages[1].SensorName);
        Assert.Equal($"{24.76.ToString("F1", CultureInfo.CurrentCulture)} C", editor.NotesPage.TemperatureAverages[1].TemperatureText);

        editor.SetTelemetryData(null);

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
        var percentages = new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var result = new SessionDetailLoadResult.Loaded(new SessionDetailData(
            new SessionTelemetryPresentationData(
                telemetry,
                Guid.NewGuid(),
                fullTrackPoints,
                trackPoints,
                400.0,
                percentages,
                DampingSpeedCutoffs.Default,
                null),
            new SessionCachePresentationData(
                "front-travel",
                "rear-travel",
                "front-velocity",
                "rear-velocity",
                null,
                null,
                percentages,
                DampingSpeedCutoffs.Default,
                false)));
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>()).Returns(result);
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Same(telemetry, editor.CurrentTelemetryData);
        Assert.Same(trackPoints, editor.CurrentTrackPoints);
        Assert.Same(fullTrackPoints, editor.MapViewModel!.FullTrackPoints);
        Assert.Equal(400.0, editor.MediaWorkspace.MediaColumnWidth);
        Assert.Equal(1, editor.DampingPage.FrontHscPercentage);
        Assert.True(editor.IsComplete);
        Assert.Equal(SurfaceStateKind.Ready, editor.SignalsWorkspace.TravelSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.SignalsWorkspace.VelocitySignalState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.SignalsWorkspace.ImuSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.MediaWorkspace.MapState.Kind);
        Assert.True(editor.AnalysisWorkspace.FrontForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearFrameVibrationState.IsHidden);
        Assert.True(editor.MediaWorkspace.HasMediaContent);
        Assert.True(editor.ScreenState.IsReady);
    }

    [AvaloniaFact]
    public async Task Loaded_InitializesRecordedSessionExtensionScope_AndUnloadedDisposesIt()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var factory = new TestRecordedSessionExtensionFactory("test");
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        SetDesktop(true);

        var editor = CreateEditor(
            snapshot,
            recordedSessionExtensionFactories: [factory]);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.NotNull(factory.Scope);
        Assert.True(factory.Scope.Initialized);
        Assert.Contains(factory.Scope.UpdatedStates, state => state.Identity.IsLoaded);

        await editor.UnloadedCommand.ExecuteAsync(null);

        Assert.True(factory.Scope.Disposed);
        Assert.False(factory.Scope.UpdatedStates.Last().Identity.IsLoaded);
    }

    [AvaloniaFact]
    public async Task Loaded_RetainsProcessedTelemetry_AndUnloadedReleasesIt()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var processedTelemetryReader = new TestSessionProcessedTelemetryReader();
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        SetDesktop(true);

        var editor = CreateEditor(
            snapshot,
            processedTelemetryReader: processedTelemetryReader);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal([snapshot.Id], processedTelemetryReader.RetainedSessionIds);
        Assert.Empty(processedTelemetryReader.ReleasedSessionIds);

        await editor.UnloadedCommand.ExecuteAsync(null);

        Assert.Equal([snapshot.Id], processedTelemetryReader.ReleasedSessionIds);
    }

    [AvaloniaFact]
    public async Task RecordedSessionExtensionScope_ReceivesHostStateUpdates()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var factory = new TestRecordedSessionExtensionFactory("test");
        var selectedRange = new TelemetryTimeRange(0.05, 0.2);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(
            snapshot,
            watch,
            recordedSessionExtensionFactories: [factory]);
        await editor.LoadedCommand.ExecuteAsync(null);
        var initialLoadedState = factory.Scope!.UpdatedStates.Last();

        editor.SetAnalysisRange(selectedRange.StartSeconds, selectedRange.EndSeconds);
        editor.SetTabActive(true);
        var domain = DomainFromSnapshot(snapshot);
        watch.OnNext(domain);
        await Task.Yield();

        Assert.Equal(snapshot.Id, initialLoadedState.Identity.SessionId);
        Assert.Equal(snapshot.Name, initialLoadedState.Identity.Name);
        Assert.Equal(telemetry.Metadata.Duration, initialLoadedState.Timeline.TelemetryDurationSeconds);
        Assert.Equal(
            selectedRange,
            factory.Scope.UpdatedStates.Last(state => state.Selection.AnalysisRange is not null).Selection.AnalysisRange);
        Assert.True(factory.Scope.UpdatedStates.Last(state => state.Identity.IsActive).Identity.IsActive);
    }

    [AvaloniaFact]
    public async Task RecordedSessionHostContext_RoutesHostCommands()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var factory = new TestRecordedSessionExtensionFactory("test");
        var selectedRange = new TelemetryTimeRange(0.05, 0.2);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(
            snapshot,
            recordedSessionExtensionFactories: [factory]);
        await editor.LoadedCommand.ExecuteAsync(null);
        var context = factory.Context!;

        context.SetAnalysisRange(selectedRange.StartSeconds, selectedRange.EndSeconds);
        context.SetTimelineVisibleRange(0.2, 0.8, context);
        context.AddError("extension error");
        context.AddNotification("extension notification");
        var lease = context.StartOperation("Extension work");
        lease.Report("Extension still working", 50);
        var beganExternalAlignment = context.TryBeginTimelineAlignment(
            RecordedSessionTimelineAlignmentTarget.ExternalMedia,
            12.0,
            subjectId: "media-a");
        var beganGpsAlignment = context.TryBeginTimelineAlignment(
            RecordedSessionTimelineAlignmentTarget.GpsTrack,
            4.0);
        var pendingAlignment = factory.Scope!.UpdatedStates.Last().Timeline.Alignment.PendingMark;
        var resolvedExternalAlignment = context.TryResolveTimelineAlignment(
            RecordedSessionTimelineAlignmentTarget.ExternalMedia,
            7.0,
            out var alignmentResolution,
            subjectId: "media-a");

        Assert.Equal(selectedRange, editor.CurrentAnalysisRange);
        Assert.Equal(0.2, editor.Timeline.VisibleRangeStart, 6);
        Assert.Equal(0.8, editor.Timeline.VisibleRangeEnd, 6);
        Assert.Contains("extension error", editor.ErrorMessages);
        Assert.Contains("extension notification", editor.Notifications);
        Assert.True(editor.ScreenState.IsReady);
        Assert.True(editor.SessionOperationState.IsVisible);
        Assert.Equal("Extension still working", editor.SessionOperationState.Message);
        Assert.Equal(50, editor.SessionOperationState.Percent);
        Assert.True(beganExternalAlignment);
        Assert.False(beganGpsAlignment);
        Assert.NotNull(pendingAlignment);
        Assert.Equal(RecordedSessionTimelineAlignmentTarget.ExternalMedia, pendingAlignment.Target);
        Assert.Equal("media-a", pendingAlignment.SubjectId);
        Assert.True(resolvedExternalAlignment);
        Assert.NotNull(alignmentResolution);
        Assert.Equal(5.0, alignmentResolution.OffsetDeltaSeconds);
        Assert.Null(factory.Scope.UpdatedStates.Last().Timeline.Alignment.PendingMark);

        lease.Complete();

        Assert.True(editor.ScreenState.IsReady);
        Assert.False(editor.SessionOperationState.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedSessionHostContext_RequestRecompute_RefreshesWindowAndUsesSourceWindowReason()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var factory = new TestRecordedSessionExtensionFactory("test");
        var windowCache = Substitute.For<IRecordedSessionDerivationWindowCache>();
        windowCache.RefreshSessionAsync(snapshot.Id).Returns(Task.CompletedTask);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        sessionCoordinator.RequestRecomputeAsync(snapshot.Id, RecomputeReason.SourceWindowChanged)
            .Returns(Task.FromResult<SessionRecomputeResult>(new SessionRecomputeResult.Recomputed(10)));
        SetDesktop(true);

        var editor = CreateEditor(
            snapshot,
            recordedSessionExtensionFactories: [factory],
            derivationWindowCache: windowCache);
        await editor.LoadedCommand.ExecuteAsync(null);

        var result = await factory.Context!.RequestRecomputeAsync(snapshot.Id);

        Assert.True(result);
        await windowCache.Received(1).RefreshSessionAsync(snapshot.Id);
        await sessionCoordinator.Received(1).RequestRecomputeAsync(snapshot.Id, RecomputeReason.SourceWindowChanged);
    }

    [AvaloniaFact]
    public async Task RecordedSessionHostContext_DeleteSession_UsesCoordinatorWithoutNavigating()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var factory = new TestRecordedSessionExtensionFactory("test");
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        sessionCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SessionDeleteResult(SessionDeleteOutcome.Deleted));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, recordedSessionExtensionFactories: [factory]);
        await editor.LoadedCommand.ExecuteAsync(null);

        var result = await factory.Context!.DeleteSessionAsync(snapshot.Id);

        Assert.True(result);
        await sessionCoordinator.Received(1).DeleteAsync(snapshot.Id);
        shell.DidNotReceive().GoBack();
    }

    [AvaloniaFact]
    public async Task RecordedSessionHostContext_OpenSessionInBackground_UsesEditorFactorySnapshot()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var part2 = TestSnapshots.Session();
        var factory = new TestRecordedSessionExtensionFactory("test");
        var editorFactory = Substitute.For<IEditorFactory>();
        sessionStore.Get(part2.Id).Returns(part2);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        SetDesktop(true);

        var editor = CreateEditor(
            snapshot,
            recordedSessionExtensionFactories: [factory],
            editorFactory: editorFactory);
        await editor.LoadedCommand.ExecuteAsync(null);

        await factory.Context!.OpenSessionInBackgroundAsync(part2.Id);

        editorFactory.Received(1).OpenSessionDetailInBackground(part2);
    }

    [AvaloniaFact]
    public async Task Loaded_InsertsExtensionPages_AndHostCanSelectThem()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var extensionPage = new TestPageViewModel("Extension");
        var factory = new TestRecordedSessionExtensionFactory(
            "test",
            scope => scope.Slots.Pages.Add(new RecordedSessionPageContribution(
                "test",
                "extension-page",
                Order: 1,
                extensionPage.DisplayName,
                extensionPage,
                RequestedIndex: 1)));
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        SetDesktop(true);

        var editor = CreateEditor(
            snapshot,
            recordedSessionExtensionFactories: [factory]);
        await editor.LoadedCommand.ExecuteAsync(null);

        var contributedPage = Assert.Single(editor.Pages, page => page.DisplayName == extensionPage.DisplayName);
        Assert.Equal(1, editor.Pages.IndexOf(contributedPage));

        factory.Context!.RequestPageSelection("extension-page");

        Assert.Equal(editor.Pages.IndexOf(contributedPage), editor.MobileWorkspace.SelectedPageIndex);
        Assert.Same(contributedPage, editor.MobileWorkspace.SelectedPage);
        Assert.Equal(extensionPage.DisplayName, editor.MobileWorkspace.SelectedPageDisplayName);

        await editor.UnloadedCommand.ExecuteAsync(null);

        Assert.DoesNotContain(contributedPage, editor.Pages);
    }

    [AvaloniaFact]
    public async Task Loaded_MediaPaneContributions_AffectHasMediaContent()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var factory = new TestRecordedSessionExtensionFactory("test");
        var observedProperties = new List<string?>();
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        SetDesktop(true);

        var editor = CreateEditor(
            snapshot,
            recordedSessionExtensionFactories: [factory]);
        ((INotifyPropertyChanged)editor.MediaWorkspace).PropertyChanged += (_, args) => observedProperties.Add(args.PropertyName);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.False(editor.MediaWorkspace.HasMediaContent);

        factory.Scope!.Slots.MediaPanes.Add(new RecordedSessionMediaPaneContribution(
            "test",
            "media-pane",
            Order: 0,
            new TestContributionViewModel()));

        Assert.True(editor.MediaWorkspace.HasMediaContent);
        Assert.Contains(nameof(ISessionMediaWorkspace.HasMediaContent), observedProperties);

        observedProperties.Clear();
        await editor.UnloadedCommand.ExecuteAsync(null);

        Assert.False(editor.MediaWorkspace.HasMediaContent);
        Assert.Contains(nameof(ISessionMediaWorkspace.HasMediaContent), observedProperties);
    }

    [AvaloniaFact]
    public async Task UnloadedThenLoaded_ReinitializesRecordedSessionExtensionScopes()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var factory = new TestRecordedSessionExtensionFactory(
            "test",
            scope => scope.Slots.MediaPanes.Add(new RecordedSessionMediaPaneContribution(
                "test",
                "media-pane",
                Order: 0,
                new TestContributionViewModel())));
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        SetDesktop(true);

        var editor = CreateEditor(
            snapshot,
            recordedSessionExtensionFactories: [factory]);

        await editor.LoadedCommand.ExecuteAsync(null);
        var firstScope = factory.Scope;
        Assert.NotNull(firstScope);
        Assert.True(editor.MediaWorkspace.HasMediaContent);

        await editor.UnloadedCommand.ExecuteAsync(null);
        Assert.True(firstScope!.Disposed);
        Assert.False(editor.MediaWorkspace.HasMediaContent);

        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(2, factory.Scopes.Count);
        Assert.NotSame(firstScope, factory.Scope);
        Assert.True(factory.Scope!.Initialized);
        Assert.True(editor.MediaWorkspace.HasMediaContent);
    }

    [AvaloniaFact]
    public async Task DampingSpeedCutoffPreview_RecomputesPercentagesAndAnalysisWithoutDirtying()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var initialCutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        var previewCutoffs = initialCutoffs.With(SuspensionType.Front, DampingSpeedCircuit.Compression, 260);
        var previewPercentages = RecordedSessionAnalysisComputer.CalculateDampingPercentages(
            telemetry,
            dampingSpeedCutoffs: previewCutoffs);
        var owner = new DampingSpeedCutoffOwner(Guid.NewGuid(), 7);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry, initialCutoffs, owner));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);
        sessionPresentationService.ClearReceivedCalls();
        sessionAnalysisService.ClearReceivedCalls();

        editor.PreviewDampingSpeedCutoff(SuspensionType.Front, DampingSpeedCircuit.Compression, 257);

        Assert.Equal(previewCutoffs, editor.AnalysisWorkspace.DampingSpeedCutoffs);
        Assert.Equal(initialCutoffs, editor.AnalysisWorkspace.PlotDampingSpeedCutoffs);
        Assert.Equal(previewPercentages, editor.AnalysisWorkspace.DampingPercentages);
        Assert.Equal(previewPercentages.FrontHscPercentage, editor.DampingPage.FrontHscPercentage);
        Assert.False(editor.IsDirty);
        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            request.DampingSpeedCutoffs == previewCutoffs &&
            request.DampingPercentages == previewPercentages));
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
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
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
        Assert.Equal(savedCutoffs, editor.AnalysisWorkspace.DampingSpeedCutoffs);
        Assert.Equal(savedCutoffs, editor.AnalysisWorkspace.PlotDampingSpeedCutoffs);
        Assert.True(editor.CanEditDampingSpeedCutoffs);
        Assert.False(editor.IsDirty);
        Assert.Empty(editor.ErrorMessages);
    }


    [AvaloniaFact]
    public async Task Loaded_OnDesktop_AppliesPersistedSignalSmoothingPreferences()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry();
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(
            preferences,
            snapshot.Id,
            new SessionPreferences(
                new SignalDisplayPreferences(
                    Travel: true,
                    Velocity: false,
                    Imu: true,
                    VelocitySmoothing: PlotSmoothingLevel.Strong),
                new AnalysisPreferences()));
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(PlotSmoothingLevel.Off, editor.PreferencesPage.TravelSignal.SelectedSmoothing);
        Assert.Equal(PlotSmoothingLevel.Strong, editor.PreferencesPage.VelocitySignal.SelectedSmoothing);
        Assert.Equal(PlotSmoothingLevel.Off, editor.PreferencesPage.ImuSignal.SelectedSmoothing);
        Assert.True(editor.PreferencesPage.TravelSignal.Available);
        Assert.True(editor.PreferencesPage.VelocitySignal.Available);
        Assert.True(editor.PreferencesPage.ImuSignal.Available);
        Assert.True(editor.SignalsWorkspace.TravelSignalState.IsReady);
        Assert.True(editor.SignalsWorkspace.VelocitySignalState.IsReady);
        Assert.True(editor.SignalsWorkspace.ImuSignalState.IsReady);
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_DisablesAndHidesImuPreference_WhenTelemetryHasNoImu()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.PreferencesPage.TravelSignal.Available);
        Assert.True(editor.PreferencesPage.VelocitySignal.Available);
        Assert.False(editor.PreferencesPage.ImuSignal.Available);
        Assert.True(editor.SignalsWorkspace.TravelSignalState.IsReady);
        Assert.True(editor.SignalsWorkspace.VelocitySignalState.IsReady);
        Assert.True(editor.SignalsWorkspace.ImuSignalState.IsHidden);
    }

    [AvaloniaFact]
    public async Task SignalPreferenceChange_PersistsAndUpdatesSignalStatesWithoutDirtyingSession()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        Func<SessionPreferences, SessionPreferences>? update = null;
        preferences.UpdateRecordedAsync(
                snapshot.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(CreateVibrationTelemetry()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();

        editor.PreferencesPage.VelocitySignal.SelectedSmoothing = PlotSmoothingLevel.Strong;

        Assert.False(editor.IsDirty);
        Assert.True(editor.SignalsWorkspace.TravelSignalState.IsReady);
        Assert.True(editor.SignalsWorkspace.VelocitySignalState.IsReady);
        Assert.True(editor.SignalsWorkspace.ImuSignalState.IsReady);
        await preferences.Received(1).UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        Assert.NotNull(update);
        var updatedPreferences = update!(SessionPreferences.Default);
        Assert.True(updatedPreferences.SignalDisplay.Velocity);
        Assert.Equal(PlotSmoothingLevel.Strong, updatedPreferences.SignalDisplay.VelocitySmoothing);
    }

    [AvaloniaFact]
    public async Task TravelHeaderAction_TogglesAirtimeWithoutDirtyingOrPersistingPreferences()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(CreateVibrationTelemetry()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();

        var action = GetRowAction(editor.TravelHeaderActions, "travel_airtime");
        action.Command!.Execute(null);

        Assert.False(editor.SignalsWorkspace.ShowAirtime);
        Assert.False(action.IsChecked);
        Assert.Equal("Show airtime", action.ToolTip);
        Assert.False(editor.IsDirty);
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());

        var velocityAction = GetRowAction(editor.VelocityHeaderActions, "velocity_airtime");
        velocityAction.Command!.Execute(null);

        Assert.True(editor.SignalsWorkspace.ShowVelocityAirtime);
        Assert.True(velocityAction.IsChecked);
        Assert.Equal("Hide airtime", velocityAction.ToolTip);
        Assert.False(editor.IsDirty);
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
    }

    [AvaloniaFact]
    public async Task SignalLayoutPreferenceChange_PersistsWithoutDirtyingSession()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        Func<SessionPreferences, SessionPreferences>? update = null;
        preferences.UpdateRecordedAsync(
                snapshot.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(CreateVibrationTelemetry()));
        SetDesktop(true);
        var signalLayout = new SignalLayoutPreferences(
        [
            new SignalLayoutRowPreferences(SignalRowIds.Imu, isExpanded: false),
            new SignalLayoutRowPreferences(
                SignalRowIds.Travel,
                children:
                [
                    new SignalLayoutRowPreferences(SignalRowIds.Velocity),
                ]),
        ]);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();

        editor.SignalLayoutPreferences = signalLayout;

        Assert.False(editor.IsDirty);
        await preferences.Received(1).UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        Assert.NotNull(update);
        Assert.Equal(signalLayout, update!(SessionPreferences.Default).SignalLayout);
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_AppliesPersistedLayoutPreferencesWithoutSavingDuringHydration()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        var layout = new SessionLayoutPreferences(
            desktopShellRows: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.SignalsMediaArea, 0.6),
                new SessionPaneSizePreference(SessionLayoutPaneIds.AnalysisSidebarArea, 0.4),
            ]),
            desktopMediaRows: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Map, 0.7),
                new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, 0.3),
            ]));
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default with { Layout = layout });
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(CreateVibrationTelemetry()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(layout, editor.LayoutPreferences);
        Assert.Equal(layout.DesktopMediaRows, editor.MediaLayoutPreferences);
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
    }

    [AvaloniaFact]
    public async Task LayoutPreferenceChange_PersistsWithoutDirtyingSession()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        Func<SessionPreferences, SessionPreferences>? update = null;
        preferences.UpdateRecordedAsync(
                snapshot.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(CreateVibrationTelemetry()));
        SetDesktop(true);
        var layout = new SessionLayoutPreferences(
            desktopAnalysisSidebarColumns: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Analysis, 0.7),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Sidebar, 0.3),
            ]));

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();

        editor.LayoutPreferences = layout;

        Assert.False(editor.IsDirty);
        await preferences.Received(1).UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        Assert.NotNull(update);
        Assert.Equal(layout, update!(SessionPreferences.Default).Layout);
    }

    [AvaloniaFact]
    public async Task ProcessingPreferenceCommit_PersistsPreferenceAndRecomputesSession()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true, updated: 5);
        var recomputedSnapshot = snapshot with { Updated = 7 };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        Func<SessionPreferences, SessionPreferences>? update = null;
        preferences.UpdateRecordedAsync(
                snapshot.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));
        var recomputeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionCoordinator.RequestRecomputeAsync(snapshot.Id, Arg.Any<RecomputeReason>())
            .Returns(_ =>
            {
                sessionStore.Get(snapshot.Id).Returns(recomputedSnapshot);
                recomputeRequested.TrySetResult();
                return new SessionRecomputeResult.Recomputed(recomputedSnapshot.Updated);
            });
        SetDesktop(true);

        var editor = CreateEditor(snapshot, watch.AsObservable(), sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        preferences.ClearReceivedCalls();

        editor.PreferencesPage.VelocityFilterWindowMilliseconds = 250;
        editor.PreferencesPage.CommitProcessingPreferenceChange();

        // The committed option persists the preference and requests a recompute; the
        // engine's store upsert then surfaces as a derived domain that advances the baseline.
        await recomputeRequested.Task.WaitAsync(TimeSpan.FromSeconds(1));
        watch.OnNext(DomainFromSnapshot(recomputedSnapshot, DerivedChangeKind.FingerprintChanged));
        await WaitForAsync(() => editor.BaselineUpdated == recomputedSnapshot.Updated);

        await preferences.Received(1).UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        Assert.NotNull(update);
        Assert.Equal(250, update!(SessionPreferences.Default).Processing.VelocityFilterWindowMilliseconds);
        await sessionCoordinator.Received(1).RequestRecomputeAsync(snapshot.Id, Arg.Any<RecomputeReason>());
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_AppliesPersistedAnalysisWithoutSavingDuringHydration()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(
            preferences,
            snapshot.Id,
            new SessionPreferences(
                new SignalDisplayPreferences(),
                new AnalysisPreferences(
                    TravelDistributionMode.DynamicSag,
                    VelocityAverageMode.StrokePeakAveraged,
                    BalanceDisplacementMode.Travel,
                    BalanceSpeedMode.HighSpeed,
                    SessionInsightsTargetProfile.DH)));
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(TravelDistributionMode.DynamicSag, editor.AnalysisWorkspace.SelectedTravelDistributionMode);
        Assert.Equal(VelocityAverageMode.StrokePeakAveraged, editor.AnalysisWorkspace.SelectedVelocityAverageMode);
        Assert.Equal(BalanceDisplacementMode.Travel, editor.AnalysisWorkspace.SelectedBalanceDisplacementMode);
        Assert.Equal(SessionInsightsTargetProfile.DH, editor.AnalysisWorkspace.SelectedSessionInsightsTargetProfile);
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_RestoringAllAnalysisModes_DoesNotFanOutInsightRequests()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, CreateNonDefaultAnalysisPreferences());
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(TravelDistributionMode.DynamicSag, editor.AnalysisWorkspace.SelectedTravelDistributionMode);
        sessionAnalysisService.DidNotReceive().Analyze(Arg.Any<SessionInsightsRequest>());
    }

    [AvaloniaFact]
    public async Task SyncedPreferenceArrival_AppliesWithoutRePersisting()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>();
        var syncStream = new Subject<SessionPreferences>();
        preferences.ObserveRecorded(snapshot.Id).Returns(syncStream);
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();
        sessionCoordinator.ClearReceivedCalls();

        var synced = SessionPreferences.Default with
        {
            Analysis = SessionPreferences.Default.Analysis with
            {
                TravelDistributionMode = TravelDistributionMode.DynamicSag,
            },
            Processing = new SessionProcessingPreferences(VelocityFilterWindowMilliseconds: 250),
        };
        syncStream.OnNext(synced);

        Assert.Equal(TravelDistributionMode.DynamicSag, editor.AnalysisWorkspace.SelectedTravelDistributionMode);
        Assert.Equal(250, editor.PreferencesPage.VelocityFilterWindowMilliseconds);
        editor.PreferencesPage.CommitProcessingPreferenceChange();
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        await sessionCoordinator.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
    }

    [AvaloniaFact]
    public async Task SyncedPreferenceArrival_WhileInsightsPageSelected_CoalescesInsightRequestAfterBatch()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>();
        var syncStream = new Subject<SessionPreferences>();
        preferences.ObserveRecorded(snapshot.Id).Returns(syncStream);
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));
        sessionAnalysisService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(CreateAnalysisResult());
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        editor.MobileWorkspace.SelectedPageIndex = editor.Pages
            .Select((page, index) => (page, index))
            .Single(entry => entry.page is SessionInsightsPageViewModel)
            .index;
        sessionAnalysisService.ClearReceivedCalls();

        syncStream.OnNext(CreateNonDefaultAnalysisPreferences());

        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            request.TravelDistributionMode == TravelDistributionMode.DynamicSag &&
            request.VelocityAverageMode == VelocityAverageMode.StrokePeakAveraged &&
            request.BalanceDisplacementMode == BalanceDisplacementMode.Travel &&
            request.BalanceSpeedMode == BalanceSpeedMode.HighSpeed &&
            request.TargetProfile == SessionInsightsTargetProfile.DH));
    }

    [AvaloniaFact]
    public async Task SyncedPreferenceArrival_UsesInjectedDispatcher_WhenOffUiThread()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var preferences = Substitute.For<ISessionPreferences>();
        var syncStream = new Subject<SessionPreferences>();
        var dispatcher = new RecordingUiThreadDispatcher(checkAccess: false);
        preferences.ObserveRecorded(snapshot.Id).Returns(syncStream);
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));
        SetDesktop(true);

        var editor = CreateEditor(
            snapshot,
            sessionPreferences: preferences,
            uiThreadDispatcher: dispatcher);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();
        var beforeInvokeCount = dispatcher.InvokeCount;

        var synced = SessionPreferences.Default with
        {
            Analysis = SessionPreferences.Default.Analysis with
            {
                TravelDistributionMode = TravelDistributionMode.DynamicSag,
            },
        };
        syncStream.OnNext(synced);

        Assert.Equal(beforeInvokeCount + 2, dispatcher.InvokeCount);
        Assert.Equal(TravelDistributionMode.DynamicSag, editor.AnalysisWorkspace.SelectedTravelDistributionMode);
        await preferences.DidNotReceive().UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
    }

    [AvaloniaFact]
    public async Task AnalysisPreferenceChange_RecomputesAnalysisAndPersistsAfterHydration()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var strokePeakPercentages = RecordedSessionAnalysisComputer.CalculateDampingPercentages(
            telemetry,
            velocityAverageMode: VelocityAverageMode.StrokePeakAveraged);
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        ConfigureRecordedPreferences(preferences, snapshot.Id, SessionPreferences.Default);
        Func<SessionPreferences, SessionPreferences>? update = null;
        preferences.UpdateRecordedAsync(
                snapshot.Id,
                Arg.Do<Func<SessionPreferences, SessionPreferences>>(value => update = value))
            .Returns(Task.CompletedTask);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, sessionPreferences: preferences);
        await editor.LoadedCommand.ExecuteAsync(null);
        preferences.ClearReceivedCalls();
        sessionAnalysisService.ClearReceivedCalls();

        editor.AnalysisWorkspace.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;

        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            request.VelocityAverageMode == VelocityAverageMode.StrokePeakAveraged &&
            request.DampingPercentages == strokePeakPercentages));
        await preferences.Received(1).UpdateRecordedAsync(snapshot.Id, Arg.Any<Func<SessionPreferences, SessionPreferences>>());
        Assert.NotNull(update);
        Assert.Equal(
            VelocityAverageMode.StrokePeakAveraged,
            update!(SessionPreferences.Default).Analysis.VelocityAverageMode);
    }

    [AvaloniaFact]
    public async Task VelocityAverageModeChange_ClearsDampingSelectionAndRequestsDampingPlusInsightsOnce()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var strokePeakPercentages = RecordedSessionAnalysisComputer.CalculateDampingPercentages(
            telemetry,
            velocityAverageMode: VelocityAverageMode.StrokePeakAveraged);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);
        var selection = CreateFrontDampingSelection(telemetry, editor.AnalysisWorkspace.SelectedVelocityAverageMode);
        editor.SelectAnalysisRangeCommand.Execute(selection);
        sessionAnalysisService.ClearReceivedCalls();

        editor.AnalysisWorkspace.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;

        Assert.Null(editor.ActiveFrontAnalysisSelection);
        Assert.Equal(VelocityAverageMode.StrokePeakAveraged, editor.AnalysisWorkspace.SelectedVelocityAverageMode);
        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            request.VelocityAverageMode == VelocityAverageMode.StrokePeakAveraged &&
            request.DampingPercentages == strokePeakPercentages));
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_DefersSessionInsightsUntilRequested()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var dampingPercentages = new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var analysis = CreateAnalysisResult();
        var result = new SessionDetailLoadResult.Loaded(new SessionDetailData(
            new SessionTelemetryPresentationData(
                telemetry,
                FullTrackId: null,
                FullTrackPoints: null,
                TrackPoints: null,
                MediaColumnWidth: null,
                DampingPercentages: dampingPercentages,
                DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
                DampingSpeedCutoffOwner: null),
            new SessionCachePresentationData(
                "front-travel",
                "rear-travel",
                "front-velocity",
                "rear-velocity",
                null,
                null,
                dampingPercentages,
                DampingSpeedCutoffs.Default,
                false)));
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>()).Returns(result);
        sessionAnalysisService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(analysis);
        sessionPresentationService.ClearReceivedCalls();
        sessionAnalysisService.ClearReceivedCalls();
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.AnalysisWorkspace.SessionInsights.State.IsHidden);
        sessionPresentationService.DidNotReceive().CalculateDampingPercentages(
            Arg.Any<TelemetryData>(),
            Arg.Any<TelemetryTimeRange?>(),
            Arg.Any<VelocityAverageMode>(),
            Arg.Any<DampingSpeedCutoffs?>());
        sessionAnalysisService.DidNotReceive().Analyze(Arg.Any<SessionInsightsRequest>());

        editor.AnalysisWorkspace.RequestSessionInsights();

        Assert.Same(analysis, editor.AnalysisWorkspace.SessionInsights);
        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            ReferenceEquals(request.TelemetryData, telemetry) &&
            request.DampingPercentages == dampingPercentages));
    }

    [AvaloniaFact]
    public void SelectingInsightsPage_RequestsSessionInsights()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var dampingPercentages = new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var analysis = CreateAnalysisResult();
        sessionAnalysisService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(analysis);
        var editor = CreateEditor(snapshot);
        editor.ApplyTelemetryDataWithoutAnalysisRecompute(telemetry);
        editor.ApplyDampingPercentages(dampingPercentages);
        sessionAnalysisService.ClearReceivedCalls();

        editor.MobileWorkspace.SelectedPageIndex = editor.Pages
            .Select((page, index) => (page, index))
            .Single(entry => entry.page is SessionInsightsPageViewModel)
            .index;

        Assert.Same(analysis, editor.AnalysisWorkspace.SessionInsights);
        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            ReferenceEquals(request.TelemetryData, telemetry) &&
            request.DampingPercentages == dampingPercentages));
    }

    [AvaloniaFact]
    public void RequestSessionInsights_BeforeTelemetryArrives_RunsAfterSuppressedTelemetryLoad()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var dampingPercentages = RecordedSessionAnalysisComputer.CalculateDampingPercentages(telemetry);
        var analysis = CreateAnalysisResult();
        sessionAnalysisService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(analysis);
        var editor = CreateEditor(snapshot);

        editor.AnalysisWorkspace.RequestSessionInsights();

        Assert.True(editor.AnalysisWorkspace.SessionInsights.State.IsHidden);
        sessionAnalysisService.DidNotReceive().Analyze(Arg.Any<SessionInsightsRequest>());

        editor.ApplyTelemetryDataWithoutAnalysisRecompute(telemetry);

        Assert.Same(analysis, editor.AnalysisWorkspace.SessionInsights);
        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            ReferenceEquals(request.TelemetryData, telemetry) &&
            request.DampingPercentages == dampingPercentages));
    }

    [AvaloniaFact]
    public void RequestSessionInsights_BeforeTelemetryArrives_RunsAfterSuppressedTelemetryLoadWithExistingAnalysisRange()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var dampingPercentages = RecordedSessionAnalysisComputer.CalculateDampingPercentages(telemetry);
        var analysis = CreateAnalysisResult();
        sessionAnalysisService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(analysis);
        var editor = CreateEditor(snapshot);
        editor.SetTelemetryData(telemetry);
        editor.SetAnalysisRange(0.2, 0.4);
        editor.SetTelemetryData(null);
        sessionAnalysisService.ClearReceivedCalls();

        editor.AnalysisWorkspace.RequestSessionInsights();

        Assert.True(editor.AnalysisWorkspace.SessionInsights.State.IsHidden);
        sessionAnalysisService.DidNotReceive().Analyze(Arg.Any<SessionInsightsRequest>());

        editor.ApplyTelemetryDataWithoutAnalysisRecompute(telemetry);

        Assert.Null(editor.CurrentAnalysisRange);
        Assert.Same(analysis, editor.AnalysisWorkspace.SessionInsights);
        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            ReferenceEquals(request.TelemetryData, telemetry) &&
            request.DampingPercentages == dampingPercentages));
    }

    [AvaloniaFact]
    public void RequestSessionInsights_BeforeTelemetryArrives_PreservesSinglePendingDemand()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var dampingPercentages = RecordedSessionAnalysisComputer.CalculateDampingPercentages(telemetry);
        sessionAnalysisService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(CreateAnalysisResult());
        var editor = CreateEditor(snapshot);

        editor.AnalysisWorkspace.RequestSessionInsights();
        editor.AnalysisWorkspace.RequestSessionInsights();

        sessionAnalysisService.DidNotReceive().Analyze(Arg.Any<SessionInsightsRequest>());

        editor.ApplyTelemetryDataWithoutAnalysisRecompute(telemetry);

        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            ReferenceEquals(request.TelemetryData, telemetry) &&
            request.DampingPercentages == dampingPercentages));
    }

    [AvaloniaFact]
    public void SelectingInsightsPage_BeforeTelemetryArrives_RunsAfterSuppressedTelemetryLoad()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = TestTelemetryData.CreateProcessed();
        var dampingPercentages = RecordedSessionAnalysisComputer.CalculateDampingPercentages(telemetry);
        var analysis = CreateAnalysisResult();
        sessionAnalysisService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(analysis);
        var editor = CreateEditor(snapshot);

        editor.MobileWorkspace.SelectedPageIndex = editor.Pages
            .Select((page, index) => (page, index))
            .Single(entry => entry.page is SessionInsightsPageViewModel)
            .index;

        Assert.True(editor.AnalysisWorkspace.SessionInsights.State.IsHidden);
        sessionAnalysisService.DidNotReceive().Analyze(Arg.Any<SessionInsightsRequest>());

        editor.ApplyTelemetryDataWithoutAnalysisRecompute(telemetry);

        Assert.Same(analysis, editor.AnalysisWorkspace.SessionInsights);
        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            ReferenceEquals(request.TelemetryData, telemetry) &&
            request.DampingPercentages == dampingPercentages));
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_WithForkAndFrameImu_SetsAllVibrationStatesReady()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = CreateVibrationTelemetry();
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.AnalysisWorkspace.FrontForkVibrationState.IsReady);
        Assert.True(editor.AnalysisWorkspace.FrontFrameVibrationState.IsReady);
        Assert.True(editor.AnalysisWorkspace.RearForkVibrationState.IsReady);
        Assert.True(editor.AnalysisWorkspace.RearFrameVibrationState.IsReady);
        Assert.Equal(SurfaceStateKind.Ready, editor.AnalysisWorkspace.FrontAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.AnalysisWorkspace.RearAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.AnalysisWorkspace.CompressionBalanceState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.AnalysisWorkspace.ReboundBalanceState.Kind);
    }

    [AvaloniaFact]
    public void SetAnalysisRange_RecomputesDampingPercentagesWithoutMarkingDirty()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry();
        var range = new TelemetryTimeRange(0.02, 0.16);
        var rangePercentages = RecordedSessionAnalysisComputer.CalculateDampingPercentages(telemetry, range);

        var editor = CreateEditor(snapshot);
        editor.SetTelemetryData(telemetry);

        editor.SetAnalysisRange(range.StartSeconds, range.EndSeconds);

        Assert.Equal(range.StartSeconds, editor.CurrentAnalysisRange?.StartSeconds);
        Assert.Equal(range.EndSeconds, editor.CurrentAnalysisRange?.EndSeconds);
        Assert.Equal(rangePercentages.FrontHscPercentage, editor.DampingPage.FrontHscPercentage);
        Assert.False(editor.IsDirty);
    }

    [AvaloniaFact]
    public void SetAnalysisRange_RecomputesAnalysisWithFreshDampingPercentagesOnce()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry();
        var range = new TelemetryTimeRange(0.02, 0.16);
        var rangePercentages = RecordedSessionAnalysisComputer.CalculateDampingPercentages(telemetry, range);

        var editor = CreateEditor(snapshot);
        editor.SetTelemetryData(telemetry);
        sessionAnalysisService.ClearReceivedCalls();

        editor.SetAnalysisRange(range.StartSeconds, range.EndSeconds);

        sessionAnalysisService.Received(1).Analyze(Arg.Is<SessionInsightsRequest>(request =>
            request.AnalysisRange.HasValue &&
            request.DampingPercentages == rangePercentages));
    }

    [AvaloniaFact]
    public void ClearAnalysisRange_RecomputesDampingPercentagesForFullSession()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry();
        var fullSessionPercentages = RecordedSessionAnalysisComputer.CalculateDampingPercentages(telemetry);

        var editor = CreateEditor(snapshot);
        editor.SetTelemetryData(telemetry);
        editor.SetAnalysisRange(0.02, 0.16);
        Assert.NotNull(editor.CurrentAnalysisRange);

        editor.ClearAnalysisRange();

        Assert.Null(editor.CurrentAnalysisRange);
        Assert.Equal(fullSessionPercentages.FrontHscPercentage, editor.DampingPage.FrontHscPercentage);
        Assert.False(editor.IsDirty);
    }

    [AvaloniaFact]
    public void ClearAnalysisRange_ClearsPendingAnalysisRangeBoundary()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var editor = CreateEditor(snapshot);
        editor.SetTelemetryData(CreateVibrationTelemetry());

        editor.SetAnalysisRangeBoundary(0.02);
        editor.ClearAnalysisRange();
        editor.SetAnalysisRangeBoundary(0.16);

        Assert.Null(editor.CurrentAnalysisRange);
    }

    [AvaloniaFact]
    public void DampingPercentagesChange_DoesNotIndependentlyRecomputeAnalysis()
    {
        var editor = CreateEditor(TestSnapshots.Session(hasProcessedData: true));
        editor.SetTelemetryData(TestTelemetryData.CreateProcessed());
        sessionAnalysisService.ClearReceivedCalls();

        editor.ApplyDampingPercentages(new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8));

        sessionAnalysisService.DidNotReceive().Analyze(Arg.Any<SessionInsightsRequest>());
    }

    [AvaloniaFact]
    public void SetAnalysisRangeBoundaryFromMarker_UsesFirstAndSecondMarkerAsRangeBoundaries()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var editor = CreateEditor(snapshot);
        editor.SetTelemetryData(CreateVibrationTelemetry());

        editor.SetAnalysisRangeBoundaryFromMarker(0.02);
        Assert.Null(editor.CurrentAnalysisRange);

        editor.SetAnalysisRangeBoundaryFromMarker(0.16);

        Assert.Equal(0.02, editor.CurrentAnalysisRange?.StartSeconds);
        Assert.Equal(0.16, editor.CurrentAnalysisRange?.EndSeconds);
    }

    [AvaloniaFact]
    public void SetAnalysisRangeBoundaryFromMarker_ReplacesNearestBoundaryForExistingRange()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var editor = CreateEditor(snapshot);
        editor.SetTelemetryData(CreateVibrationTelemetry());
        editor.SetAnalysisRange(0.02, 0.18);

        editor.SetAnalysisRangeBoundaryFromMarker(0.05);

        Assert.Equal(0.05, editor.CurrentAnalysisRange?.StartSeconds);
        Assert.Equal(0.18, editor.CurrentAnalysisRange?.EndSeconds);
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_WithOnlyForkImu_HidesFrameVibrationStates()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = CreateVibrationTelemetry(frameImu: false);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.AnalysisWorkspace.FrontForkVibrationState.IsReady);
        Assert.True(editor.AnalysisWorkspace.RearForkVibrationState.IsReady);
        Assert.True(editor.AnalysisWorkspace.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearFrameVibrationState.IsHidden);
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_WithoutImu_HidesVibrationStates()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = CreateVibrationTelemetry(forkImu: false, frameImu: false);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.AnalysisWorkspace.FrontForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearFrameVibrationState.IsHidden);
    }

    [AvaloniaFact]
    public async Task Loaded_OnDesktop_WithImuAndNoStrokes_ShowsNoDataStates()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var telemetry = CreateVibrationTelemetry(frontStrokes: false, rearStrokes: false);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(LoadedDesktopResult(telemetry));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.Equal(SurfaceStateKind.NoData, editor.AnalysisWorkspace.FrontAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.NoData, editor.AnalysisWorkspace.RearAnalysisState.Kind);
        Assert.Equal(SurfaceIndicatorKind.None, editor.AnalysisWorkspace.FrontAnalysisState.Indicator);
        Assert.Equal("No analysis data.", editor.AnalysisWorkspace.FrontAnalysisState.Message);
        Assert.Equal(SurfaceStateKind.NoData, editor.AnalysisWorkspace.FrontForkVibrationState.Kind);
        Assert.Equal(SurfaceStateKind.NoData, editor.AnalysisWorkspace.FrontFrameVibrationState.Kind);
        Assert.Equal(SurfaceStateKind.NoData, editor.AnalysisWorkspace.RearForkVibrationState.Kind);
        Assert.Equal(SurfaceStateKind.NoData, editor.AnalysisWorkspace.RearFrameVibrationState.Kind);
    }

    [AvaloniaFact]
    public async Task WatchRefresh_OnDesktop_WhenTelemetryLaterPending_ClearsVibrationStates()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry();
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(LoadedDesktopResult(telemetry)),
                Task.FromResult<SessionDetailLoadResult>(IncompleteResult(snapshot.Id)));
        SetDesktop(true);

        var editor = CreateEditor(snapshot, watch.AsObservable());
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.True(editor.AnalysisWorkspace.FrontForkVibrationState.IsReady);

        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.FingerprintChanged));

        await WaitForAsync(() => editor.AnalysisWorkspace.FrontForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearFrameVibrationState.IsHidden);
    }

    [AvaloniaFact]
    public async Task Loaded_OnMobile_AppliesCacheResult_AndRemovesBalancePageWhenUnavailable()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var result = LoadedResult(new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDampingPercentages(1, null, 2, null, 3, null, 4, null),
            DampingSpeedCutoffs.Default,
            false),
            TestTelemetryData.CreateProcessed());
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        SetDesktop(false);

        var editor = CreateEditor(snapshot, deferDomainHandlingWhenInactive: false);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));
        var springPage = editor.Pages.OfType<SpringPageViewModel>().Single();

        Assert.NotNull(editor.CurrentTelemetryData);
        Assert.Equal("front-travel", springPage.FrontTravelDistribution);
        Assert.Equal("front-velocity", editor.DampingPage.FrontVelocityDistribution);
        Assert.True(editor.IsComplete);
        Assert.Equal(SurfaceStateKind.Ready, editor.SignalsWorkspace.TravelSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.SignalsWorkspace.VelocitySignalState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.SignalsWorkspace.ImuSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, springPage.FrontDistributionState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, springPage.RearDistributionState.Kind);
        Assert.Equal(SurfaceStateKind.Ready, editor.AnalysisWorkspace.FrontAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.AnalysisWorkspace.RearAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.AnalysisWorkspace.CompressionBalanceState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.AnalysisWorkspace.ReboundBalanceState.Kind);
        Assert.True(editor.AnalysisWorkspace.FrontForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearFrameVibrationState.IsHidden);
        Assert.False(editor.MediaWorkspace.HasMediaContent);
        Assert.DoesNotContain(editor.Pages, page => page.DisplayName == "Balance");
    }

    [AvaloniaFact]
    public async Task Loaded_OnMobile_IncompleteLocalData_SetsIncompleteScreenState()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var result = new SessionDetailLoadResult.IncompleteLocalData(
            snapshot.Id,
            new MissingSessionData(
                ProcessedTelemetryBlob: true,
                RecordedSourceMissingOrHashMismatch: false));
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        SetDesktop(false);

        var editor = CreateEditor(snapshot, deferDomainHandlingWhenInactive: false);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));

        Assert.Equal(SessionScreenStateKind.IncompleteLocalData, editor.ScreenState.Kind);
        Assert.Contains("processed telemetry", editor.ScreenState.Message);
        Assert.Contains("Run sync", editor.ScreenState.Message);
        Assert.True(editor.IsComplete);
    }

    [AvaloniaFact]
    public async Task Loaded_OnMobile_FromCacheWithNoStrokes_ShowsNoDataAnalysis()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var telemetry = CreateVibrationTelemetry(
            frontStrokes: false,
            rearStrokes: false,
            forkImu: false,
            frameImu: false);
        var result = LoadedResult(new SessionCachePresentationData(
            null,
            null,
            null,
            null,
            null,
            null,
            SessionDampingPercentages.Empty,
            DampingSpeedCutoffs.Default,
            false),
            telemetry);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        SetDesktop(false);

        var editor = CreateEditor(snapshot, deferDomainHandlingWhenInactive: false);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));

        Assert.Equal(SurfaceStateKind.NoData, editor.AnalysisWorkspace.FrontAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.NoData, editor.AnalysisWorkspace.RearAnalysisState.Kind);
        Assert.Equal(SurfaceIndicatorKind.None, editor.AnalysisWorkspace.FrontAnalysisState.Indicator);
        Assert.Equal("No analysis data.", editor.AnalysisWorkspace.FrontAnalysisState.Message);
    }

    [AvaloniaFact]
    public async Task Loaded_OnMobile_IncompleteLocalData_HidesExtendedAnalysis()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        var result = IncompleteResult(snapshot.Id);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        SetDesktop(false);

        var editor = CreateEditor(snapshot, deferDomainHandlingWhenInactive: false);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));

        Assert.True(editor.AnalysisWorkspace.FrontAnalysisState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearAnalysisState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.SessionInsights.State.IsHidden);
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
        var result = LoadedResult(new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDampingPercentages(1, null, 2, null, 3, null, 4, null),
            DampingSpeedCutoffs.Default,
            false),
            TestTelemetryData.CreateProcessed(),
            new SessionTrackPresentationData(Guid.NewGuid(), fullTrackPoints, trackPoints, 400));
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        SetDesktop(false);

        var editor = CreateEditor(snapshot, deferDomainHandlingWhenInactive: false);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));

        Assert.Same(trackPoints, editor.CurrentTrackPoints);
        Assert.Same(fullTrackPoints, editor.MapViewModel!.FullTrackPoints);
        Assert.Equal(400, editor.MediaWorkspace.MediaColumnWidth);
        Assert.Equal(SurfaceStateKind.Ready, editor.MediaWorkspace.MapState.Kind);
        Assert.True(editor.MediaWorkspace.HasMediaContent);
        Assert.Same(trackPoints, editor.MapViewModel!.SessionTrackPoints);
    }

    [AvaloniaFact]
    public async Task Loaded_WhenLocalDataIncomplete_EntersIncompleteScreenState()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        await editor.LoadedCommand.ExecuteAsync(null);

        Assert.False(editor.IsComplete);
        Assert.Equal(SessionScreenStateKind.IncompleteLocalData, editor.ScreenState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.SignalsWorkspace.TravelSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.SignalsWorkspace.VelocitySignalState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.SignalsWorkspace.ImuSignalState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.AnalysisWorkspace.FrontAnalysisState.Kind);
        Assert.Equal(SurfaceStateKind.Hidden, editor.AnalysisWorkspace.RearAnalysisState.Kind);
        Assert.True(editor.AnalysisWorkspace.FrontForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.FrontFrameVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearForkVibrationState.IsHidden);
        Assert.True(editor.AnalysisWorkspace.RearFrameVibrationState.IsHidden);
        Assert.Equal(SurfaceStateKind.Hidden, editor.MediaWorkspace.MapState.Kind);
        Assert.Empty(editor.ErrorMessages);
    }

    [AvaloniaFact]
    public async Task Loaded_WhenCoordinatorFails_SetsScreenError()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(new SessionDetailLoadResult.Failed("boom"));
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
        var pending = new TaskCompletionSource<SessionDetailLoadResult>();

        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => AwaitWithCancellation(
                pending.Task,
                callInfo.ArgAt<CancellationToken>(2)));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        var loadTask = editor.LoadedCommand.ExecuteAsync(null);
        await Task.Yield();

        editor.UnloadedCommand.Execute(null);
        pending.SetResult(LoadedDesktopResult(telemetry));

        await loadTask;

        Assert.Null(editor.CurrentTelemetryData);
        Assert.False(editor.IsComplete);
    }

    [AvaloniaFact]
    public async Task Unloaded_OnMobile_CancelsInFlightLoad_AndDropsCacheResult()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var pending = new TaskCompletionSource<SessionDetailLoadResult>();

        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => AwaitWithCancellation(
                pending.Task,
                callInfo.ArgAt<CancellationToken>(2)));
        SetDesktop(false);

        var editor = CreateEditor(snapshot, deferDomainHandlingWhenInactive: false);
        var loadTask = editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));
        await Task.Yield();

        editor.UnloadedCommand.Execute(null);
        pending.SetResult(LoadedResult(new SessionCachePresentationData(
            "front-travel",
            null,
            "front-velocity",
            null,
            null,
            null,
            new SessionDampingPercentages(1, null, 2, null, 3, null, 4, null),
            DampingSpeedCutoffs.Default,
            false),
            TestTelemetryData.CreateProcessed(),
            new SessionTrackPresentationData(null, null, null, null)));

        await loadTask;

        var springPage = editor.Pages.OfType<SpringPageViewModel>().Single();
        Assert.Null(springPage.FrontTravelDistribution);
        Assert.Null(editor.DampingPage.FrontVelocityDistribution);
        Assert.False(editor.IsComplete);
    }

    [AvaloniaFact]
    public async Task SaveAfter_IncompleteLocalData_PreservesHasProcessedData()
    {
        var snapshot = TestSnapshots.Session(updated: 5, hasProcessedData: true);
        var result = IncompleteResult(snapshot.Id);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(result);
        sessionCoordinator.SaveAsync(Arg.Any<Session>(), 5)
            .Returns(new SessionSaveResult.Saved(11));
        SetDesktop(false);

        var editor = CreateEditor(snapshot, deferDomainHandlingWhenInactive: false);
        await editor.LoadedCommand.ExecuteAsync(new Rect(0, 0, 400, 300));
        editor.Name = "renamed";

        await editor.SaveCommand.ExecuteAsync(null);

        await sessionCoordinator.Received(1).SaveAsync(
            Arg.Is<Session>(session => session.Id == snapshot.Id && session.HasProcessedData),
            5);
    }

    [AvaloniaFact]
    public async Task Loaded_WhenAlreadyLoaded_DoesNotStartSecondLoad()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var firstTelemetry = TestTelemetryData.CreateProcessed();
        var firstPending = new TaskCompletionSource<SessionDetailLoadResult>();

        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => AwaitWithCancellation(firstPending.Task, callInfo.ArgAt<CancellationToken>(2)));
        SetDesktop(true);

        var editor = CreateEditor(snapshot);
        var firstLoad = editor.LoadedCommand.ExecuteAsync(null);
        await Task.Yield();

        var secondLoad = editor.LoadedCommand.ExecuteAsync(null);
        firstPending.SetResult(LoadedDesktopResult(firstTelemetry));

        await Task.WhenAll(firstLoad, secondLoad);

        Assert.Same(firstTelemetry, editor.CurrentTelemetryData);
        await sessionCoordinator.Received(1).LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task WatchRefreshes_AreCoalescedWhileLoadIsInFlight()
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: false);
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var initialResult = IncompleteResult(snapshot.Id);
        var refreshLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshPending = new TaskCompletionSource<SessionDetailLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalLoadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalPending = new TaskCompletionSource<SessionDetailLoadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finalTelemetry = TestTelemetryData.CreateProcessed();
        var finalResultApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callCount = 0;

        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var cancellationToken = callInfo.ArgAt<CancellationToken>(2);
                callCount++;
                return callCount switch
                {
                    1 => Task.FromResult<SessionDetailLoadResult>(initialResult),
                    2 => StartRefreshLoad(refreshPending.Task, cancellationToken),
                    _ => StartFinalLoad(finalPending.Task, cancellationToken)
                };

                Task<SessionDetailLoadResult> StartRefreshLoad(
                    Task<SessionDetailLoadResult> task,
                    CancellationToken token)
                {
                    refreshLoadStarted.TrySetResult();
                    return AwaitWithCancellation(task, token);
                }

                Task<SessionDetailLoadResult> StartFinalLoad(
                    Task<SessionDetailLoadResult> task,
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
            if (ReferenceEquals(editor.CurrentTelemetryData, finalTelemetry) &&
                editor.DampingPage.FrontHscPercentage == 1)
            {
                finalResultApplied.TrySetResult();
            }
        }

        ((INotifyPropertyChanged)editor.SignalsWorkspace).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IRecordedSessionSignalsWorkspace.TelemetryData))
            {
                MarkWhenFinalStateApplied();
            }
        };
        editor.DampingPage.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DampingPageViewModel.FrontHscPercentage))
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
        refreshPending.SetResult(LoadedDesktopResult(TestTelemetryData.CreateProcessed()));

        await finalLoadStarted.Task;
        finalPending.SetResult(LoadedDesktopResult(finalTelemetry));

        await finalResultApplied.Task;

        // The presentation load is a cancel-and-replace operation: each watch refresh
        // that arrives while a load is in flight cancels it and starts a fresh load
        // (initial + three refreshes = four invocations). Only the final, uncancelled
        // load applies its result, so rapid refreshes never surface stale telemetry.
        Assert.Same(finalTelemetry, editor.CurrentTelemetryData);
        await sessionCoordinator.Received(4).LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>());
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
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        dialogService.ShowChoiceAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(call => call.Arg<IReadOnlyList<DialogChoice>>().First(c => c.Label == "Recompute").Id);
        sessionCoordinator.RequestRecomputeAsync(snapshot.Id, Arg.Any<RecomputeReason>())
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

        // The reconciler prompts for the stale-on-open domain and requests a
        // recompute. The resulting baseline advance is driven by the watch reaction,
        // which other tests cover; here we assert the forward-only prompt-and-request.
        await dialogService.Received(1).ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
        await sessionCoordinator.Received(1).RequestRecomputeAsync(snapshot.Id, RecomputeReason.StaleOnOpen);
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

        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        dialogService.ShowChoiceAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(call => call.Arg<IReadOnlyList<DialogChoice>>().First(c => c.Label == "Recompute").Id);
        sessionCoordinator.RequestRecomputeAsync(snapshot.Id, Arg.Any<RecomputeReason>())
            .Returns(_ =>
            {
                recomputeStarted.TrySetResult();
                return recomputeResult.Task;
            });

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);

        // First stale emission prompts and requests the recompute.
        watch.OnNext(staleDomain);
        await recomputeStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // Re-emitting the same stale signature while the recompute is in flight is
        // suppressed by the running prompt.
        watch.OnNext(staleDomain);
        watch.OnNext(staleDomain);
        await Task.Yield();

        // After the recompute resolves, the same stale signature stays deduplicated:
        // the prompter only re-arms when a different signature or a fresh state arrives.
        recomputeResult.SetResult(new SessionRecomputeResult.Recomputed(recomputedSnapshot.Updated));
        await Task.Yield();
        watch.OnNext(staleDomain);
        await Task.Yield();

        await dialogService.Received(1).ShowChoiceAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<DialogChoice>>());
        await sessionCoordinator.Received(1).RequestRecomputeAsync(snapshot.Id, Arg.Any<RecomputeReason>());
    }

    [AvaloniaFact]
    public async Task Loaded_AfterUnload_ReplaysStalePromptForSameDomain()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", hasProcessedData: true, updated: 5);
        var watch = new ReplaySubject<RecordedSessionDomainSnapshot>(1);
        var promptCount = 0;
        var secondPrompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var staleDomain = DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.Initial,
            new SessionStaleness.DependencyHashChanged());

        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        dialogService.ShowChoiceAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(call =>
            {
                promptCount++;
                if (promptCount == 2)
                {
                    secondPrompt.TrySetResult();
                }

                return call.Arg<IReadOnlyList<DialogChoice>>().First(c => c.Label == "Cancel").Id;
            });

        watch.OnNext(staleDomain);
        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);

        await editor.LoadedCommand.ExecuteAsync(null);
        await WaitForAsync(() => promptCount == 1);

        await editor.UnloadedCommand.ExecuteAsync(null);
        await editor.LoadedCommand.ExecuteAsync(null);
        await secondPrompt.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Equal(2, promptCount);
        await sessionCoordinator.DidNotReceive().RequestRecomputeAsync(snapshot.Id, Arg.Any<RecomputeReason>());
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
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                loadCount++;
                return loadCount == 1
                    ? LoadedDesktopResult(oldTelemetry)
                    : LoadedDesktopResult(freshTelemetry);
            });
        dialogService.ShowChoiceAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(call => call.Arg<IReadOnlyList<DialogChoice>>().First(c => c.Label == "Recompute").Id);
        var recomputeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionCoordinator.RequestRecomputeAsync(snapshot.Id, Arg.Any<RecomputeReason>())
            .Returns(_ =>
            {
                sessionStore.Get(snapshot.Id).Returns(recomputedSnapshot);
                recomputeRequested.TrySetResult();
                return new SessionRecomputeResult.Recomputed(recomputedSnapshot.Updated);
            });

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);
        Assert.Same(oldTelemetry, editor.CurrentTelemetryData);

        ((INotifyPropertyChanged)editor.SignalsWorkspace).PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IRecordedSessionSignalsWorkspace.TelemetryData))
            {
                telemetryChanges.Add(editor.CurrentTelemetryData);
            }
        };

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.Initial,
            new SessionStaleness.DependencyHashChanged()));

        // The stale-on-open prompt requests the recompute; the engine's store upsert
        // then surfaces as a fresh derived domain that reloads the presentation.
        await recomputeRequested.Task.WaitAsync(TimeSpan.FromSeconds(1));
        watch.OnNext(DomainFromSnapshot(recomputedSnapshot, DerivedChangeKind.FingerprintChanged));

        await WaitForAsync(() => ReferenceEquals(editor.CurrentTelemetryData, freshTelemetry));

        Assert.Contains(null, telemetryChanges);
        Assert.Same(freshTelemetry, editor.CurrentTelemetryData);
        await sessionCoordinator.Received(2).LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>());
    }

    [AvaloniaFact]
    public async Task CloseCommand_DisposesProjectionWatchAfterDeclinedStalePrompt()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", hasProcessedData: true, updated: 5);
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        dialogService.ShowChoiceAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(call => call.Arg<IReadOnlyList<DialogChoice>>().First(c => c.Label == "Cancel").Id);

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.Initial,
            new SessionStaleness.DependencyHashChanged()));
        await Task.Yield();

        await dialogService.Received(1).ShowChoiceAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<DialogChoice>>());

        await editor.CloseCommand.ExecuteAsync(null);
        shell.Received(1).Close(editor);

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.DependencyChanged,
            new SessionStaleness.DependencyHashChanged()));
        await Task.Yield();

        await dialogService.Received(1).ShowChoiceAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<DialogChoice>>());
        await sessionCoordinator.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
    }

    private static void AssertDefaultHiddenAirtimeAction(
        IReadOnlyList<SignalRowAction> actions,
        bool isVisible,
        string expectedId)
    {
        var action = GetRowAction(actions, expectedId);
        Assert.False(isVisible);
        Assert.Equal(expectedId, action.Id);
        Assert.Equal(SignalRowActionKind.Toggle, action.Kind);
        Assert.False(action.IsChecked);
        Assert.Equal("Show airtime", action.ToolTip);
        Assert.NotNull(action.Command);
    }

    private static void AssertDefaultDisabledAnalysisSelectionAction(
        IReadOnlyList<SignalRowAction> actions,
        bool isVisible,
        string expectedId)
    {
        var action = GetRowAction(actions, expectedId);
        Assert.False(isVisible);
        Assert.Equal(SignalRowActionKind.Toggle, action.Kind);
        Assert.False(action.IsChecked);
        Assert.False(action.IsEnabled);
        Assert.Equal("Select a bin to highlight matching signal spans.", action.ToolTip);
        Assert.NotNull(action.Command);
    }

    private static SignalRowAction GetRowAction(
        IReadOnlyList<SignalRowAction> actions,
        string id)
    {
        return Assert.Single(actions, action => action.Id == id);
    }

    private static DampingRangeSelection CreateFrontDampingSelection(
        TelemetryData telemetry,
        VelocityAverageMode averageMode)
    {
        var histogram = TelemetryStatistics.CalculateVelocityHistogram(
            telemetry,
            SuspensionType.Front,
            new VelocityStatisticsOptions(null, averageMode));

        for (var velocityBinIndex = 0; velocityBinIndex < histogram.Values.Count; velocityBinIndex++)
        {
            var travelValues = histogram.Values[velocityBinIndex];
            for (var travelBinIndex = 0; travelBinIndex < travelValues.Length; travelBinIndex++)
            {
                if (travelValues[travelBinIndex] > 0)
                {
                    return new DampingRangeSelection(
                        SuspensionType.Front,
                        averageMode,
                        velocityBinIndex,
                        travelBinIndex,
                        travelBinIndex);
                }
            }
        }

        Assert.Fail("Expected a non-empty front damping histogram bin.");
        return default!;
    }

    private static TelemetryPlotContextMenuAction GetAutozoomAction(SessionDetailViewModel editor)
    {
        return GetPlotContextAction(editor, "zoom-selection");
    }

    private static TelemetryPlotContextMenuAction GetPlotContextAction(
        SessionDetailViewModel editor,
        string id)
    {
        return Assert.Single(
            editor.SignalPlotContextMenuActionsBySignalRowId[SignalRowIds.Travel],
            action => action.Id == id);
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
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);

        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.Initial,
            new SessionStaleness.MissingRawSource()));
        await Task.Yield();

        Assert.Empty(editor.ErrorMessages);
        await dialogService.DidNotReceive().ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
        await sessionCoordinator.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
    }

    [AvaloniaFact]
    public async Task RuntimeDerivedChange_RecomputeConfirmation_PreservesDirtyDraft()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", description: "persisted", hasProcessedData: true, updated: 5);
        var recomputedSnapshot = snapshot with { Updated = 8 };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var recomputeCalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        dialogService.ShowChoiceAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(call => call.Arg<IReadOnlyList<DialogChoice>>().First(c => c.Label == "Recompute").Id);
        sessionCoordinator.RequestRecomputeAsync(snapshot.Id, Arg.Any<RecomputeReason>())
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

        // A dependency change prompts a recompute; the user confirms.
        watch.OnNext(DomainFromSnapshot(
            snapshot,
            DerivedChangeKind.DependencyChanged,
            new SessionStaleness.DependencyHashChanged()));
        await recomputeCalled.Task.WaitAsync(TimeSpan.FromSeconds(1));

        // The recompute result surfaces as a pure derived change: it refreshes the
        // derived state and advances the baseline, but keeps the unsaved draft.
        watch.OnNext(DomainFromSnapshot(recomputedSnapshot, DerivedChangeKind.FingerprintChanged));
        await WaitForAsync(() => editor.BaselineUpdated == recomputedSnapshot.Updated);

        Assert.True(editor.IsDirty);
        Assert.Equal("dirty draft", editor.DescriptionText);
        Assert.Equal(recomputedSnapshot.Updated, editor.BaselineUpdated);
    }

    [AvaloniaFact]
    public async Task RuntimeDerivedChange_DeclinedRecompute_KeepsDirtyDraft()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", description: "persisted", hasProcessedData: true, updated: 5);
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        dialogService.ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(call => call.Arg<IReadOnlyList<DialogChoice>>().First(c => c.Label == "Cancel").Id);

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
        await sessionCoordinator.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
    }

    [AvaloniaFact]
    public async Task RuntimeDerivedChange_DefersRecomputePrompt_UntilDesktopTabIsActive()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", hasProcessedData: true, updated: 5);
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var promptShown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        dialogService.ShowChoiceAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<DialogChoice>>())
            .Returns(call =>
            {
                promptShown.TrySetResult();
                return call.Arg<IReadOnlyList<DialogChoice>>().First(c => c.Label == "Cancel").Id;
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

        await dialogService.DidNotReceive().ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());

        editor.SetTabActive(true);
        await promptShown.Task.WaitAsync(TimeSpan.FromSeconds(1));

        await dialogService.Received(1).ShowChoiceAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyList<DialogChoice>>());
        await sessionCoordinator.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
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

        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
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
        Assert.Same(oldTelemetry, editor.CurrentTelemetryData);

        // A recompute that produced fresh telemetry surfaces as a pure derived
        // change; the editor reloads its presentation and advances the baseline.
        watch.OnNext(DomainFromSnapshot(updatedSnapshot, DerivedChangeKind.FingerprintChanged));

        await WaitForAsync(() => ReferenceEquals(editor.CurrentTelemetryData, freshTelemetry));

        Assert.Equal(updatedSnapshot.Updated, editor.BaselineUpdated);
        await sessionCoordinator.Received(2).LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>());
        await dialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task RuntimeFreshUpdate_DeclinedDirtyReload_KeepsDraftAndCurrentPresentation()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", description: "persisted", hasProcessedData: true, updated: 5);
        var updatedSnapshot = snapshot with { Updated = 8, Description = "remote" };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var oldTelemetry = TestTelemetryData.CreateProcessed();

        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
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

        // External metadata edit while the local draft is dirty: the editor prompts
        // to discard, and on decline keeps the draft and holds the baseline back.
        watch.OnNext(DomainFromSnapshot(updatedSnapshot, DerivedChangeKind.SessionMetadataChanged));
        await Task.Yield();

        Assert.True(editor.IsDirty);
        Assert.Equal("dirty draft", editor.DescriptionText);
        Assert.Equal(snapshot.Updated, editor.BaselineUpdated);
        Assert.Same(oldTelemetry, editor.CurrentTelemetryData);
        await sessionCoordinator.Received(1).LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>());
        await dialogService.Received(1).ShowConfirmationAsync(
            Arg.Any<string>(),
            Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task DerivedChange_AfterDeclinedMetadataConflict_HoldsBaselineSoNextSaveStillConflicts()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", description: "persisted", hasProcessedData: true, updated: 5);
        var metadataSnapshot = snapshot with { Updated = 8, Description = "remote" };
        var derivedSnapshot = snapshot with { Updated = 9 };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(IncompleteResult(snapshot.Id));
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        editor.DescriptionText = "dirty draft";
        Assert.True(editor.IsDirty);

        // An external metadata edit lands on the dirty draft; the user declines to discard.
        watch.OnNext(DomainFromSnapshot(metadataSnapshot, DerivedChangeKind.SessionMetadataChanged));
        await Task.Yield();
        Assert.Equal(snapshot.Updated, editor.BaselineUpdated);

        // A later derived-only change refreshes telemetry but must not advance the
        // baseline past the still-unacknowledged metadata edit.
        watch.OnNext(DomainFromSnapshot(derivedSnapshot, DerivedChangeKind.FingerprintChanged));
        await Task.Yield();
        Assert.Equal(snapshot.Updated, editor.BaselineUpdated);

        // The held-back baseline means the next save still detects the external edit
        // as a conflict instead of silently overwriting it.
        sessionCoordinator.SaveAsync(Arg.Any<Session>(), Arg.Any<long>())
            .Returns(new SessionSaveResult.Conflict(metadataSnapshot));
        await editor.SaveCommand.ExecuteAsync(null);
        await sessionCoordinator.Received(1).SaveAsync(Arg.Any<Session>(), snapshot.Updated);
    }

    [AvaloniaFact]
    public async Task CombinedMetadataAndDerivedChange_RefreshesTelemetry_AndStillPromptsForMetadata()
    {
        var snapshot = TestSnapshots.Session(name: "trail run", description: "persisted", hasProcessedData: true, updated: 5);
        var combinedSnapshot = snapshot with { Updated = 8, Description = "remote" };
        var watch = new Subject<RecordedSessionDomainSnapshot>();
        var oldTelemetry = TestTelemetryData.CreateProcessed();
        var freshTelemetry = TestTelemetryData.CreateProcessed();
        var loadCount = 0;

        sessionCoordinator.LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                loadCount++;
                return loadCount == 1
                    ? LoadedDesktopResult(oldTelemetry)
                    : LoadedDesktopResult(freshTelemetry);
            });
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        var editor = CreateEditor(snapshot, watch.AsObservable(), isDesktop: true);
        await editor.LoadedCommand.ExecuteAsync(null);
        watch.OnNext(DomainFromSnapshot(snapshot, DerivedChangeKind.Initial));
        editor.DescriptionText = "dirty draft";
        Assert.True(editor.IsDirty);

        // One emission carries BOTH a derived change and an external metadata change.
        // The two axes are handled orthogonally: the derived telemetry refreshes
        // regardless, while the metadata change against the dirty draft still prompts.
        watch.OnNext(DomainFromSnapshot(
            combinedSnapshot,
            DerivedChangeKind.FingerprintChanged | DerivedChangeKind.SessionMetadataChanged));

        await WaitForAsync(() => ReferenceEquals(editor.CurrentTelemetryData, freshTelemetry));

        // Derived axis applied: plots never show stale telemetry even with a pending prompt.
        await sessionCoordinator.Received(2).LoadDetailAsync(snapshot.Id, Arg.Any<SessionPresentationDimensions>(), Arg.Any<CancellationToken>());
        // Metadata axis decided independently: prompt shown, declined -> draft kept and
        // the baseline held back so the next save still detects the conflict.
        await dialogService.Received(1).ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
        Assert.True(editor.IsDirty);
        Assert.Equal("dirty draft", editor.DescriptionText);
        Assert.Equal(snapshot.Updated, editor.BaselineUpdated);
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
        null,
        staleness ?? new SessionStaleness.Current(),
        changeKind);

    private static SessionDetailLoadResult LoadedDesktopResult(
        TelemetryData telemetry,
        DampingSpeedCutoffs? dampingSpeedCutoffs = null,
        DampingSpeedCutoffOwner? dampingSpeedCutoffOwner = null,
        SessionDampingPercentages? dampingPercentages = null)
    {
        var cutoffs = dampingSpeedCutoffs ?? DampingSpeedCutoffs.Default;
        var percentages = dampingPercentages ?? new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        return new SessionDetailLoadResult.Loaded(new SessionDetailData(
            new SessionTelemetryPresentationData(
                telemetry,
                null,
                null,
                null,
                null,
                percentages,
                cutoffs,
                dampingSpeedCutoffOwner),
            new SessionCachePresentationData(
                FrontTravelDistribution: "front-travel",
                RearTravelDistribution: "rear-travel",
                FrontVelocityDistribution: "front-velocity",
                RearVelocityDistribution: "rear-velocity",
                CompressionBalance: "compression-balance",
                ReboundBalance: "rebound-balance",
                DampingPercentages: percentages,
                DampingSpeedCutoffs: cutoffs,
                BalanceAvailable: true,
                DampingSpeedCutoffOwner: dampingSpeedCutoffOwner)));
    }

    private static SessionDetailLoadResult LoadedResult(
        SessionCachePresentationData cachePresentation,
        TelemetryData telemetry,
        SessionTrackPresentationData? trackData = null) =>
        new SessionDetailLoadResult.Loaded(new SessionDetailData(
            new SessionTelemetryPresentationData(
                telemetry,
                trackData?.FullTrackId,
                trackData?.FullTrackPoints,
                trackData?.TrackPoints,
                trackData?.MediaColumnWidth,
                cachePresentation.DampingPercentages,
                cachePresentation.DampingSpeedCutoffs,
                cachePresentation.DampingSpeedCutoffOwner),
            cachePresentation));

    private static SessionDetailLoadResult IncompleteResult(Guid sessionId) =>
        new SessionDetailLoadResult.IncompleteLocalData(
            sessionId,
            new MissingSessionData(
                ProcessedTelemetryBlob: true,
                RecordedSourceMissingOrHashMismatch: false));

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

    private static SessionPreferences CreateNonDefaultAnalysisPreferences() =>
        new(
            new SignalDisplayPreferences(),
            new AnalysisPreferences(
                TravelDistributionMode.DynamicSag,
                VelocityAverageMode.StrokePeakAveraged,
                BalanceDisplacementMode.Travel,
                BalanceSpeedMode.HighSpeed,
                SessionInsightsTargetProfile.DH));

    private static ISessionPreferences CreateSessionPreferences()
    {
        var preferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        preferences.GetRecordedAsync(Arg.Any<Guid>()).Returns(Task.FromResult(SessionPreferences.Default));
        preferences.UpdateRecordedAsync(Arg.Any<Guid>(), Arg.Any<Func<SessionPreferences, SessionPreferences>>())
            .Returns(Task.CompletedTask);
        return preferences;
    }

    private static SessionInsightsResult CreateAnalysisResult()
    {
        return new SessionInsightsResult(
            SurfacePresentationState.Ready,
            [new SessionInsightsFinding(
                SessionInsightsCategory.DataQuality,
                SessionInsightsSeverity.Info,
                SessionInsightsConfidence.Low,
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

    private sealed class TestPageViewModel(string displayName)
        : PageViewModelBase(displayName), IRecordedSessionPageContributionViewModel
    {
    }
}

using System.Reactive;
using System.Reactive.Subjects;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class RecordedSessionEditorActionsTests
{
    [Fact]
    public void SelectPageIndex_ClampsNegativeIndex()
    {
        using var actions = new RecordedSessionEditorActions();
        var intents = Subscribe(actions);

        actions.SelectPageIndex(-3);

        var intent = Assert.IsType<RecordedSessionEditorIntent.SelectPageIndex>(Assert.Single(intents));
        Assert.Equal(0, intent.PageIndex);
    }

    [Fact]
    public void SetAnalysisRangeBoundary_IgnoresNonFiniteValues()
    {
        using var actions = new RecordedSessionEditorActions();
        var intents = Subscribe(actions);

        actions.SetAnalysisRangeBoundary(double.NaN);
        actions.SetAnalysisRangeBoundary(double.PositiveInfinity);
        actions.SetAnalysisRangeBoundary(12.5);

        var intent = Assert.IsType<RecordedSessionEditorIntent.SetAnalysisRangeBoundary>(Assert.Single(intents));
        Assert.Equal(12.5, intent.Seconds);
    }

    [Fact]
    public void SetDampingSpeedCutoffs_ClampsValues()
    {
        using var actions = new RecordedSessionEditorActions();
        var intents = Subscribe(actions);

        actions.SetDampingSpeedCutoffs(new DampingSpeedCutoffs(
            new DampingSpeedCutoffSide(-10, DampingSpeedCutoffs.MaximumMmPerSecond + 10),
            new DampingSpeedCutoffSide(125, 250)));

        var intent = Assert.IsType<RecordedSessionEditorIntent.SetDampingSpeedCutoffs>(Assert.Single(intents));
        Assert.Equal(DampingSpeedCutoffs.MinimumMmPerSecond, intent.Cutoffs.Front.CompressionMmPerSecond);
        Assert.Equal(DampingSpeedCutoffs.MaximumMmPerSecond, intent.Cutoffs.Front.ReboundMmPerSecond);
        Assert.Equal(125, intent.Cutoffs.Rear.CompressionMmPerSecond);
        Assert.Equal(250, intent.Cutoffs.Rear.ReboundMmPerSecond);
    }

    [Fact]
    public void Methods_EmitTypedIntentPayloads()
    {
        using var actions = new RecordedSessionEditorActions();
        var intents = Subscribe(actions);
        var range = new TelemetryTimeRange(1, 3);
        var selection = new DeepTravelRangeSelection(
            SuspensionType.Front,
            new TelemetryRangeSelection.BinRange(0, 0, 10, IsFirst: true, IsLast: false));
        var signalDisplay = new SignalDisplayPreferences(Travel: false);
        var signalLayout = SignalLayoutPreferences.Default;
        var layout = SessionLayoutPreferences.Default;

        actions.SetAnalysisRange(range);
        actions.SetAnalysisRangeStartBoundary(2);
        actions.SetAnalysisRangeEndBoundary(4);
        actions.ClearAnalysisRange();
        actions.SelectAnalysisRange(selection);
        actions.ClearAnalysisSelection();
        actions.RequestSessionInsights();
        actions.RequestDampingPercentages();
        actions.RefreshSelectedPageAnalysis();
        actions.SetTravelDistributionMode(TravelDistributionMode.ActiveSuspension);
        actions.SetBalanceDisplacementMode(BalanceDisplacementMode.Zenith);
        actions.SetBalanceSpeedMode(BalanceSpeedMode.Both);
        actions.SetVelocityAverageMode(VelocityAverageMode.SampleAveraged);
        actions.SetSessionInsightsTargetProfile(SessionInsightsTargetProfile.Enduro);
        actions.SetSignalDisplayPreferences(signalDisplay);
        actions.SetSignalLayoutPreferences(signalLayout);
        actions.SetLayoutPreferences(layout);

        Assert.Collection(
            intents,
            intent => Assert.Equal(range, Assert.IsType<RecordedSessionEditorIntent.SetAnalysisRange>(intent).Range),
            intent => Assert.Equal(2, Assert.IsType<RecordedSessionEditorIntent.SetAnalysisRangeStartBoundary>(intent).Seconds),
            intent => Assert.Equal(4, Assert.IsType<RecordedSessionEditorIntent.SetAnalysisRangeEndBoundary>(intent).Seconds),
            intent => Assert.IsType<RecordedSessionEditorIntent.ClearAnalysisRange>(intent),
            intent => Assert.Equal(selection, Assert.IsType<RecordedSessionEditorIntent.SelectAnalysisRange>(intent).Selection),
            intent => Assert.IsType<RecordedSessionEditorIntent.ClearAnalysisSelection>(intent),
            intent => Assert.IsType<RecordedSessionEditorIntent.RequestSessionInsights>(intent),
            intent => Assert.IsType<RecordedSessionEditorIntent.RequestDampingPercentages>(intent),
            intent => Assert.IsType<RecordedSessionEditorIntent.RefreshSelectedPageAnalysis>(intent),
            intent => Assert.Equal(TravelDistributionMode.ActiveSuspension, Assert.IsType<RecordedSessionEditorIntent.SetTravelDistributionMode>(intent).Mode),
            intent => Assert.Equal(BalanceDisplacementMode.Zenith, Assert.IsType<RecordedSessionEditorIntent.SetBalanceDisplacementMode>(intent).Mode),
            intent => Assert.Equal(BalanceSpeedMode.Both, Assert.IsType<RecordedSessionEditorIntent.SetBalanceSpeedMode>(intent).Mode),
            intent => Assert.Equal(VelocityAverageMode.SampleAveraged, Assert.IsType<RecordedSessionEditorIntent.SetVelocityAverageMode>(intent).Mode),
            intent => Assert.Equal(SessionInsightsTargetProfile.Enduro, Assert.IsType<RecordedSessionEditorIntent.SetSessionInsightsTargetProfile>(intent).Profile),
            intent => Assert.Equal(signalDisplay, Assert.IsType<RecordedSessionEditorIntent.SetSignalDisplayPreferences>(intent).Preferences),
            intent => Assert.Equal(signalLayout, Assert.IsType<RecordedSessionEditorIntent.SetSignalLayoutPreferences>(intent).Preferences),
            intent => Assert.Equal(layout, Assert.IsType<RecordedSessionEditorIntent.SetLayoutPreferences>(intent).Preferences));
    }

    [Fact]
    public void Dispose_CompletesIntentStream_AndIgnoresLaterCalls()
    {
        var actions = new RecordedSessionEditorActions();
        var intents = new List<RecordedSessionEditorIntent>();
        var completed = false;
        using var subscription = actions.Intents.Subscribe(
            intents.Add,
            () => completed = true);

        actions.Dispose();
        actions.SelectPageIndex(1);

        Assert.True(completed);
        Assert.Empty(intents);
    }

    [Fact]
    public void StateController_ReplaysLatestDistinctState()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        driver.PageCounts.OnNext(5);
        driver.Actions.SelectPageIndex(2);
        driver.Actions.SelectPageIndex(2);

        var observed = new List<RecordedSessionEditorState>();
        using var subscription = driver.Controller.State.Subscribe(observed.Add);

        driver.Actions.SelectPageIndex(3);
        driver.Actions.SelectPageIndex(3);

        Assert.Collection(
            observed,
            state => Assert.Equal(2, state.Intent.SelectedPageIndex),
            state => Assert.Equal(3, state.Intent.SelectedPageIndex));
    }

    [Fact]
    public void StateController_DerivesSelectedPageIndex_FromActionsAndPageCount()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<int>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Intent.SelectedPageIndex));

        driver.PageCounts.OnNext(3);
        driver.Actions.SelectPageIndex(2);
        driver.Actions.SelectPageIndex(99);
        driver.PageCounts.OnNext(2);
        driver.Actions.SelectPageIndex(-3);
        driver.PageCounts.OnNext(0);

        Assert.Collection(
            observed,
            selectedPageIndex => Assert.Equal(0, selectedPageIndex),
            selectedPageIndex => Assert.Equal(2, selectedPageIndex),
            selectedPageIndex => Assert.Equal(1, selectedPageIndex),
            selectedPageIndex => Assert.Equal(0, selectedPageIndex));
    }

    [Fact]
    public void StateController_DerivesPreferenceIntentState_FromActionsAndPreferenceReplay()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorIntentState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Intent));
        var replayedAnalysis = new AnalysisPreferences(
            TravelDistributionMode.DynamicSag,
            VelocityAverageMode.StrokePeakAveraged,
            BalanceDisplacementMode.Travel,
            BalanceSpeedMode.HighSpeed,
            SessionInsightsTargetProfile.DH);
        var replayedSignalDisplay = new SignalDisplayPreferences(
            Travel: false,
            VelocitySmoothing: PlotSmoothingLevel.Strong);
        var replayedSignalLayout = new SignalLayoutPreferences(
        [
            new SignalLayoutRowPreferences(SignalRowIds.Imu, isExpanded: false),
        ]);
        var replayedLayout = new SessionLayoutPreferences(
            desktopMediaRows: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Map, 0.65),
                new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, 0.35),
            ]));
        var replayedPreferences = SessionPreferences.Default with
        {
            Analysis = replayedAnalysis,
            SignalDisplay = replayedSignalDisplay,
            SignalLayout = replayedSignalLayout,
            Layout = replayedLayout,
        };
        var userSignalDisplay = replayedSignalDisplay with { Speed = false };
        var userSignalLayout = new SignalLayoutPreferences(
        [
            new SignalLayoutRowPreferences(SignalRowIds.Speed),
        ]);
        var userLayout = new SessionLayoutPreferences(
            desktopAnalysisSidebarColumns: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Analysis, 0.7),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Sidebar, 0.3),
            ]));

        driver.Actions.SetTravelDistributionMode(TravelDistributionMode.DynamicSag);
        driver.PreferenceReplays.OnNext(replayedPreferences);
        driver.Actions.SetBalanceSpeedMode(BalanceSpeedMode.LowSpeed);
        driver.Actions.SetSignalDisplayPreferences(userSignalDisplay);
        driver.Actions.SetSignalLayoutPreferences(userSignalLayout);
        driver.Actions.SetLayoutPreferences(userLayout);

        Assert.Equal(7, observed.Count);
        Assert.Equal(TravelDistributionMode.ActiveSuspension, observed[0].SelectedTravelDistributionMode);
        Assert.Equal(TravelDistributionMode.DynamicSag, observed[1].SelectedTravelDistributionMode);
        Assert.Equal(replayedAnalysis.TravelDistributionMode, observed[2].SelectedTravelDistributionMode);
        Assert.Equal(replayedAnalysis.VelocityAverageMode, observed[2].SelectedVelocityAverageMode);
        Assert.Equal(replayedAnalysis.BalanceDisplacementMode, observed[2].SelectedBalanceDisplacementMode);
        Assert.Equal(replayedAnalysis.BalanceSpeedMode, observed[2].SelectedBalanceSpeedMode);
        Assert.Equal(replayedAnalysis.SessionInsightsTargetProfile, observed[2].SelectedSessionInsightsTargetProfile);
        Assert.Equal(replayedSignalDisplay, observed[2].SignalDisplayPreferences);
        Assert.Equal(replayedSignalLayout, observed[2].SignalLayoutPreferences);
        Assert.Equal(replayedLayout, observed[2].LayoutPreferences);
        Assert.Equal(BalanceSpeedMode.LowSpeed, observed[3].SelectedBalanceSpeedMode);
        Assert.Equal(userSignalDisplay, observed[4].SignalDisplayPreferences);
        Assert.Equal(userSignalLayout, observed[5].SignalLayoutPreferences);
        Assert.Equal(userLayout, observed[6].LayoutPreferences);
        Assert.Equal(TravelDistributionMode.DynamicSag, observed[^1].SelectedTravelDistributionMode);
        Assert.Equal(BalanceSpeedMode.LowSpeed, observed[^1].SelectedBalanceSpeedMode);
        Assert.Equal(userSignalDisplay, observed[^1].SignalDisplayPreferences);
        Assert.Equal(userSignalLayout, observed[^1].SignalLayoutPreferences);
        Assert.Equal(userLayout, observed[^1].LayoutPreferences);
    }

    [Fact]
    public void StateController_DerivesAnalysisRange_FromActionsAndCurrentTelemetry()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<TelemetryTimeRange?>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Intent.AnalysisRange));
        var telemetry = TestTelemetryData.CreateMinimal(duration: 2.0);

        driver.Actions.SetAnalysisRange(new TelemetryTimeRange(1.0, 3.0));
        driver.PublishTelemetry(telemetry);
        driver.Actions.ClearAnalysisRange();

        var transitions = AdjacentDistinct(observed);

        Assert.Collection(
            transitions,
            range => Assert.Null(range),
            range =>
            {
                Assert.Equal(1.0, range?.StartSeconds);
                Assert.Equal(2.0, range?.EndSeconds);
            },
            range => Assert.Null(range));
    }

    [Fact]
    public void StateController_DerivesAnalysisRange_FromStartAndEndBoundaryActions()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorIntentState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Intent));
        var telemetry = TestTelemetryData.CreateMinimal(duration: 10.0);

        driver.PublishTelemetry(telemetry);
        driver.Actions.SetAnalysisRangeStartBoundary(3.0);
        driver.Actions.SetAnalysisRangeEndBoundary(7.0);

        var transitions = AdjacentDistinct(observed
            .Where(state => state.AnalysisRange is not null || state.PendingAnalysisRangeBoundary is not null)
            .Select(state => (state.AnalysisRange, state.PendingAnalysisRangeBoundary)));

        Assert.Collection(
            transitions,
            state =>
            {
                Assert.Null(state.AnalysisRange);
                Assert.Equal(3.0, state.PendingAnalysisRangeBoundary);
            },
            state =>
            {
                Assert.Equal(new TelemetryTimeRange(3.0, 7.0), state.AnalysisRange);
                Assert.Null(state.PendingAnalysisRangeBoundary);
            });
    }

    [Fact]
    public void StateController_ClearAnalysisRange_ClearsPendingBoundary()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorIntentState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Intent));
        var telemetry = TestTelemetryData.CreateMinimal(duration: 10.0);

        driver.PublishTelemetry(telemetry);
        driver.Actions.SetAnalysisRangeStartBoundary(3.0);
        driver.Actions.ClearAnalysisRange();

        Assert.Contains(observed, state => state.PendingAnalysisRangeBoundary == 3.0);
        var last = observed[^1];
        Assert.Null(last.AnalysisRange);
        Assert.Null(last.PendingAnalysisRangeBoundary);
    }

    [Fact]
    public void StateController_GenericAnalysisRangeBoundary_ReplacesNearestBoundary()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<TelemetryTimeRange?>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Intent.AnalysisRange));
        var telemetry = TestTelemetryData.CreateMinimal(duration: 10.0);

        driver.PublishTelemetry(telemetry);
        driver.Actions.SetAnalysisRange(new TelemetryTimeRange(2.0, 8.0));
        driver.Actions.SetAnalysisRangeBoundary(3.0);
        driver.Actions.SetAnalysisRangeBoundary(7.0);

        Assert.Equal(new TelemetryTimeRange(3.0, 7.0), observed.Last());
    }

    [Fact]
    public void StateController_DerivesDampingSpeedCutoffs_FromActions()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<DampingSpeedCutoffs>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Intent.DampingSpeedCutoffs));
        var cutoffs = DampingSpeedCutoffs.FromValues(110, 220, 330, 440);

        driver.Actions.SetDampingSpeedCutoffs(cutoffs);

        Assert.Collection(
            observed,
            value => Assert.Equal(DampingSpeedCutoffs.Default, value),
            value => Assert.Equal(cutoffs, value));
    }

    [Fact]
    public void StateController_DerivesScreenState_FromLoadPresentation_AndOperationStateFromInput()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorPresentationState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Presentation));
        var operation = SessionOperationPresentationState.Progress("Working", 25);
        var incomplete = new RecordedSessionLoadPresentation.IncompleteLocalData(
            new MissingSessionData(
                ProcessedTelemetryBlob: true,
                RecordedSourceMissingOrHashMismatch: false),
            HasProcessedData: true);

        driver.LoadPresentations.OnNext(incomplete);
        driver.OperationStates.OnNext(operation);

        var transitions = AdjacentDistinct(observed.Select(state => (state.ScreenState, state.OperationState)));

        Assert.Collection(
            transitions,
            state =>
            {
                Assert.Equal(SessionScreenPresentationState.Ready, state.ScreenState);
                Assert.Equal(SessionOperationPresentationState.Hidden, state.OperationState);
            },
            state =>
            {
                Assert.Equal(SessionScreenStateKind.IncompleteLocalData, state.ScreenState.Kind);
                Assert.Equal(SessionOperationPresentationState.Hidden, state.OperationState);
            },
            state =>
            {
                Assert.Equal(SessionScreenStateKind.IncompleteLocalData, state.ScreenState.Kind);
                Assert.Equal(operation, state.OperationState);
            });
    }

    [Fact]
    public void StateController_DerivesMediaPresentationState_FromLoadAndMediaInputs()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorPresentationState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Presentation));
        const double mediaColumnWidth = 480;
        const string mediaUrl = "session-media.mp4";
        var telemetry = TestTelemetryData.CreateProcessed();
        List<TrackPoint> trackPoints = [new TrackPoint(1, 2, 3, 4)];

        driver.PublishLoadedData(
            telemetryData: telemetry,
            trackPoints: trackPoints,
            mediaColumnWidth: mediaColumnWidth);
        driver.MediaPaneStates.OnNext(SurfacePresentationState.Ready);
        driver.MediaUrls.OnNext(mediaUrl);

        Assert.Equal(SurfacePresentationState.Hidden, observed[0].MapState);
        Assert.Equal(SurfacePresentationState.Hidden, observed[0].MediaPaneState);
        Assert.Null(observed[0].MediaColumnWidth);
        Assert.Null(observed[0].MediaUrl);
        Assert.Equal(SurfacePresentationState.Ready, observed[^1].MapState);
        Assert.Equal(SurfacePresentationState.Ready, observed[^1].MediaPaneState);
        Assert.Equal(mediaColumnWidth, observed[^1].MediaColumnWidth);
        Assert.Equal(mediaUrl, observed[^1].MediaUrl);
    }

    [Fact]
    public void StateController_DerivesLoadingPresentation_FromLoadPresentation()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = driver.Controller.State.Subscribe(observed.Add);

        driver.LoadPresentations.OnNext(new RecordedSessionLoadPresentation.Loading(MapExpected: true));

        var state = observed[^1];
        Assert.IsType<RecordedSessionLoadPresentation.Loading>(state.Load);
        Assert.Equal(SurfaceStateKind.Loading, state.Presentation.MapState.Kind);
        Assert.Equal(SurfaceStateKind.Loading, state.Presentation.Signals.Travel.Kind);
        Assert.Equal(SurfaceStateKind.Loading, state.Presentation.Signals.Velocity.Kind);
        Assert.Equal(SurfaceStateKind.Loading, state.Presentation.Signals.Speed.Kind);
        Assert.Equal(SurfaceStateKind.Loading, state.Presentation.Signals.Elevation.Kind);
        Assert.Equal(SurfaceStateKind.Loading, state.Presentation.Analysis.FrontAnalysis.Kind);
        Assert.Equal(SurfaceStateKind.Loading, state.Presentation.Analysis.RearAnalysis.Kind);
        Assert.False(state.Presentation.SignalAvailability.Travel);
        Assert.False(state.Presentation.SignalAvailability.Speed);
    }

    [Fact]
    public void StateController_DerivesLoadedSignalAvailability_FromTelemetry()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = driver.Controller.State.Subscribe(observed.Add);
        var telemetry = TestTelemetryData.CreateWithImu();

        driver.LoadPresentations.OnNext(CreateLoadedPresentation(telemetry));

        var state = observed[^1];
        Assert.IsType<RecordedSessionLoadPresentation.Loaded>(state.Load);
        Assert.Same(telemetry, state.TelemetryData);
        Assert.True(state.Presentation.SignalAvailability.Travel);
        Assert.True(state.Presentation.SignalAvailability.Velocity);
        Assert.True(state.Presentation.SignalAvailability.Imu);
        Assert.True(state.Presentation.SignalAvailability.PitchRoll);
        Assert.False(state.Presentation.SignalAvailability.Speed);
        Assert.False(state.Presentation.SignalAvailability.Elevation);
        Assert.Equal(SurfaceStateKind.Ready, state.Presentation.Signals.Travel.Kind);
        Assert.Equal(SurfaceStateKind.Ready, state.Presentation.Signals.Velocity.Kind);
        Assert.Equal(SurfaceStateKind.Ready, state.Presentation.Signals.Imu.Kind);
        Assert.Equal(SurfaceStateKind.Ready, state.Presentation.Signals.PitchRoll.Kind);
        Assert.True(state.Presentation.Signals.Speed.IsHidden);
        Assert.True(state.Presentation.Signals.Elevation.IsHidden);
    }

    [Fact]
    public void StateController_DerivesLoadedTrackSignalAvailability_FromTrackPoints()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = driver.Controller.State.Subscribe(observed.Add);
        List<TrackPoint> trackPoints =
        [
            new TrackPoint(0, 1, 2, 10, speed: 5),
            new TrackPoint(1, 2, 3, 12, speed: 6),
        ];

        driver.LoadPresentations.OnNext(CreateLoadedPresentation(
            TestTelemetryData.CreateMinimal(),
            trackPoints: trackPoints,
            fullTrackId: Guid.NewGuid()));

        var state = observed[^1];
        Assert.Same(trackPoints, state.TrackPoints);
        Assert.True(state.Presentation.SignalAvailability.Speed);
        Assert.True(state.Presentation.SignalAvailability.Elevation);
        Assert.Equal(SurfaceStateKind.Ready, state.Presentation.Signals.Speed.Kind);
        Assert.Equal(SurfaceStateKind.Ready, state.Presentation.Signals.Elevation.Kind);
        Assert.Equal(SurfaceStateKind.Ready, state.Presentation.MapState.Kind);
    }

    [Fact]
    public void StateController_RederivesAnalysisPresentation_WhenAnalysisRangeChanges()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedAnalysisPresentationState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Presentation.Analysis));
        var telemetry = TestTelemetryData.CreateMinimal(duration: 4.0);

        driver.LoadPresentations.OnNext(CreateLoadedPresentation(telemetry));
        driver.Actions.SetAnalysisRange(new TelemetryTimeRange(1.0, 2.0));

        Assert.Contains(observed, state => state.FrontAnalysis.Message == "No analysis data.");
        Assert.Equal("No analysis data for the selected range.", observed[^1].FrontAnalysis.Message);
        Assert.Equal("No analysis data for the selected range.", observed[^1].RearAnalysis.Message);
    }

    [Fact]
    public void StateController_DerivesIncompleteAndFailedPresentation_FromLoadPresentation()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = driver.Controller.State.Subscribe(observed.Add);

        driver.LoadPresentations.OnNext(new RecordedSessionLoadPresentation.IncompleteLocalData(
            new MissingSessionData(
                ProcessedTelemetryBlob: true,
                RecordedSourceMissingOrHashMismatch: true),
            HasProcessedData: false));

        var incomplete = observed[^1];
        Assert.IsType<RecordedSessionLoadPresentation.IncompleteLocalData>(incomplete.Load);
        Assert.Equal(SessionScreenStateKind.IncompleteLocalData, incomplete.Presentation.ScreenState.Kind);
        Assert.Contains("processed telemetry", incomplete.Presentation.ScreenState.Message);
        Assert.Contains("recorded source", incomplete.Presentation.ScreenState.Message);
        Assert.True(incomplete.Presentation.MapState.IsHidden);
        Assert.True(incomplete.Presentation.Analysis.FrontAnalysis.IsHidden);
        Assert.False(incomplete.Presentation.SignalAvailability.Travel);

        driver.LoadPresentations.OnNext(new RecordedSessionLoadPresentation.Failed("boom"));

        var failed = observed[^1];
        Assert.IsType<RecordedSessionLoadPresentation.Failed>(failed.Load);
        Assert.Equal(SessionScreenStateKind.Error, failed.Presentation.ScreenState.Kind);
        Assert.Contains("boom", failed.Presentation.ScreenState.Message);
        Assert.True(failed.Presentation.MapState.IsHidden);
        Assert.True(failed.Presentation.Analysis.FrontAnalysis.IsHidden);
        Assert.False(failed.Presentation.SignalAvailability.Travel);
    }

    [Fact]
    public void StateController_DerivesAnalysisPresentationState_FromAnalysisInputs()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedAnalysisPresentationState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Presentation.Analysis));
        var analysis = new RecordedAnalysisPresentationState(
            FrontAnalysis: SurfacePresentationState.Ready,
            RearAnalysis: SurfacePresentationState.Loading("Rear"),
            CompressionBalance: SurfacePresentationState.Ready,
            ReboundBalance: SurfacePresentationState.Hidden,
            FrontForkVibration: SurfacePresentationState.Ready,
            FrontFrameVibration: SurfacePresentationState.Hidden,
            RearForkVibration: SurfacePresentationState.Ready,
            RearFrameVibration: SurfacePresentationState.Hidden);

        driver.AnalysisPresentationStates.OnNext(analysis);

        Assert.Equal(2, observed.Count);
        Assert.True(observed[0].FrontAnalysis.IsHidden);
        Assert.Equal(analysis, observed[1]);
        Assert.Equal(analysis, observed[^1]);
    }

    [Fact]
    public void StateController_DerivesAnalysisPresentationDetails_FromInputs()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorPresentationState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Presentation));
        var percentages = new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var plotCutoffs = DampingSpeedCutoffs.FromValues(120, 240, 360, 480);
        var insights = new SessionInsightsResult(SurfacePresentationState.Loading("Insights"), []);

        driver.DampingPercentages.OnNext(percentages);
        driver.PlotDampingSpeedCutoffs.OnNext(plotCutoffs);
        driver.CanEditDampingSpeedCutoffs.OnNext(true);
        driver.SessionInsights.OnNext(insights);

        Assert.Equal(5, observed.Count);
        Assert.Equal(SessionDampingPercentages.Empty, observed[0].DampingPercentages);
        Assert.Equal(DampingSpeedCutoffs.Default, observed[0].PlotDampingSpeedCutoffs);
        Assert.False(observed[0].CanEditDampingSpeedCutoffs);
        Assert.Equal(SessionInsightsResult.Hidden, observed[0].SessionInsights);
        Assert.Equal(percentages, observed[^1].DampingPercentages);
        Assert.Equal(plotCutoffs, observed[^1].PlotDampingSpeedCutoffs);
        Assert.True(observed[^1].CanEditDampingSpeedCutoffs);
        Assert.Equal(insights, observed[^1].SessionInsights);
    }

    [Fact]
    public void StateController_DerivesSignalPresentation_FromInputs()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSignalPresentationState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Presentation.Signals));
        SignalRowAction[] travelHeaderActions = [new SignalRowAction { Id = "travel" }];
        SignalRowAction[] velocityHeaderActions = [new SignalRowAction { Id = "velocity" }];
        var signals = new RecordedSignalPresentationState(
            Travel: SurfacePresentationState.Ready,
            Velocity: SurfacePresentationState.Loading("Loading velocity"),
            Imu: SurfacePresentationState.Hidden,
            PitchRoll: SurfacePresentationState.WaitingForData("Waiting"),
            Speed: SurfacePresentationState.Ready,
            Elevation: SurfacePresentationState.Hidden,
            ShowAirtime: true,
            ShowVelocityAirtime: true,
            ShowImuAirtime: false,
            ShowPitchRollAirtime: true,
            ShowSpeedAirtime: false,
            ShowElevationAirtime: true,
            ShowAnalysisSelection: true,
            ShowVelocityAnalysisSelection: false,
            ShowImuAnalysisSelection: true,
            ShowPitchRollAnalysisSelection: false,
            ShowSpeedAnalysisSelection: true,
            ShowElevationAnalysisSelection: false,
            TravelHeaderActions: travelHeaderActions,
            VelocityHeaderActions: velocityHeaderActions,
            ImuHeaderActions: [],
            PitchRollHeaderActions: [],
            SpeedHeaderActions: [],
            ElevationHeaderActions: []);

        driver.SignalPresentationStates.OnNext(signals);

        Assert.Equal(2, observed.Count);
        Assert.Equal(SurfacePresentationState.Hidden, observed[0].Travel);
        Assert.False(observed[0].ShowAirtime);
        Assert.Equal(signals, observed[1]);
        Assert.Equal(signals, observed[^1]);
        Assert.Same(travelHeaderActions, observed[^1].TravelHeaderActions);
        Assert.Same(velocityHeaderActions, observed[^1].VelocityHeaderActions);
    }

    [Fact]
    public void StateController_DerivesAnalysisSelection_FromInputs()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<AnalysisSelectionState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.AnalysisSelection));
        var selection = new DeepTravelRangeSelection(
            SuspensionType.Front,
            new TelemetryRangeSelection.BinRange(0, 0, 10, IsFirst: true, IsLast: false));
        TelemetryHighlightRange[] highlightRanges = [new TelemetryHighlightRange(1, 2, SuspensionType.Front)];
        var analysisSelection = new AnalysisSelectionState(
            ActiveFront: selection,
            ActiveRear: null,
            HighlightRanges: highlightRanges);

        driver.AnalysisSelections.OnNext(analysisSelection);

        Assert.Equal(2, observed.Count);
        Assert.Null(observed[0].ActiveFront);
        Assert.Empty(observed[0].HighlightRanges);
        Assert.Equal(analysisSelection, observed[1]);
        Assert.Equal(analysisSelection, observed[^1]);
        Assert.Same(highlightRanges, observed[^1].HighlightRanges);
    }

    [Fact]
    public void StateController_DerivesAnalysisSelection_FromSelectionIntents()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<AnalysisSelectionState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.AnalysisSelection));
        var telemetry = TestTelemetryData.CreateProcessed();
        var selection = new DeepTravelRangeSelection(
            SuspensionType.Front,
            new TelemetryRangeSelection.BinRange(0, 0, 10, IsFirst: true, IsLast: false));

        driver.PublishTelemetry(telemetry);
        driver.Actions.SelectAnalysisRange(selection);

        var selected = observed[^1];
        Assert.Equal(selection, selected.ActiveFront);
        Assert.Null(selected.ActiveRear);
    }

    [Fact]
    public void StateController_DerivesAnalysisSelectionToggleAndClear_FromSelectionIntents()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<AnalysisSelectionState>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.AnalysisSelection));
        var telemetry = TestTelemetryData.CreateProcessed();
        var selection = new DeepTravelRangeSelection(
            SuspensionType.Front,
            new TelemetryRangeSelection.BinRange(0, 0, 10, IsFirst: true, IsLast: false));

        driver.PublishTelemetry(telemetry);
        driver.Actions.SelectAnalysisRange(selection);
        driver.Actions.SelectAnalysisRange(selection);

        Assert.Null(observed[^1].ActiveFront);
        Assert.Null(observed[^1].ActiveRear);
        Assert.Empty(observed[^1].HighlightRanges);

        driver.Actions.SelectAnalysisRange(selection);
        driver.Actions.ClearAnalysisSelection();

        Assert.Null(observed[^1].ActiveFront);
        Assert.Null(observed[^1].ActiveRear);
        Assert.Empty(observed[^1].HighlightRanges);
    }

    [Fact]
    public void StateController_DerivesLoadedData_FromLoadPresentation()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = driver.Controller.State.Subscribe(observed.Add);
        var session = TestSnapshots.Session();
        var telemetry = TestTelemetryData.CreateProcessed();
        List<TrackPoint> fullTrackPoints = [new TrackPoint(1, 2, 3, 4)];
        List<TrackPoint> trackPoints = [new TrackPoint(5, 6, 7, 8)];
        driver.PublishLoadedData(
            session,
            telemetry,
            fullTrackPoints,
            trackPoints);
        driver.Actions.SetTravelDistributionMode(TravelDistributionMode.DynamicSag);
        var expectedTimelineContext = new TrackTimeRange(
            telemetry.Metadata.Timestamp + SessionTrackProjection.NormalizeGpsOffsetSeconds(session.GpsOffsetSeconds),
            telemetry.Metadata.Duration);

        Assert.Null(observed[0].Session);
        Assert.Null(observed[0].TelemetryData);
        Assert.Same(session, observed[1].Session);
        Assert.Same(telemetry, observed[1].TelemetryData);
        Assert.Same(fullTrackPoints, observed[1].FullTrackPoints);
        Assert.Same(trackPoints, observed[1].TrackPoints);
        Assert.Equal(expectedTimelineContext, observed[1].TrackTimelineContext);
        Assert.Same(session, observed[^1].Session);
        Assert.Same(telemetry, observed[^1].TelemetryData);
        Assert.Same(fullTrackPoints, observed[^1].FullTrackPoints);
        Assert.Same(trackPoints, observed[^1].TrackPoints);
        Assert.Equal(expectedTimelineContext, observed[^1].TrackTimelineContext);
    }

    [Fact]
    public void StateController_DerivesDomain_FromInputs()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var observed = new List<RecordedSessionDomainSnapshot?>();
        using var subscription = driver.Controller.State.Subscribe(state => observed.Add(state.Domain));
        var domain = CreateDomain(TestSnapshots.Session(updated: 11), DerivedChangeKind.Initial);

        driver.DomainStates.OnNext(domain);

        Assert.Null(observed[0]);
        Assert.Same(domain, observed[1]);
        Assert.Same(domain, observed[^1]);
    }

    [Fact]
    public void MapMediaSync_EmitsLoadedDataChanges()
    {
        using var states = new Subject<RecordedSessionEditorState>();
        var effects = new List<RecordedSessionEditorEffect>();
        using var subscription = RecordedSessionEditorEffects.MapMediaSync(states)
            .Subscribe(effects.Add);
        var session = TestSnapshots.Session();
        var telemetry = TestTelemetryData.CreateProcessed();
        List<TrackPoint> fullTrackPoints = [new TrackPoint(1, 2, 3, 4)];
        List<TrackPoint> trackPoints = [new TrackPoint(5, 6, 7, 8)];
        var timelineContext = new TrackTimeRange(10, 20);
        var initial = CreateState(selectedPageIndex: 0);
        var loaded = initial with
        {
            Session = session,
            TelemetryData = telemetry,
            FullTrackPoints = fullTrackPoints,
            TrackPoints = trackPoints,
            TrackTimelineContext = timelineContext,
        };

        states.OnNext(initial);
        states.OnNext(initial);
        states.OnNext(loaded);

        Assert.Collection(
            effects,
            effect => Assert.Null(Assert.IsType<RecordedSessionEditorEffect.SyncMapMedia>(effect).LoadedData.Session),
            effect =>
            {
                var sync = Assert.IsType<RecordedSessionEditorEffect.SyncMapMedia>(effect);
                Assert.Same(session, sync.LoadedData.Session);
                Assert.Same(telemetry, sync.LoadedData.TelemetryData);
                Assert.Same(fullTrackPoints, sync.LoadedData.FullTrackPoints);
                Assert.Same(trackPoints, sync.LoadedData.TrackPoints);
                Assert.Equal(timelineContext, sync.LoadedData.TrackTimelineContext);
            });
    }

    [Fact]
    public void CommandRefresh_EmitsLoadedDataChanges()
    {
        using var states = new Subject<RecordedSessionEditorState>();
        var effects = new List<RecordedSessionEditorEffect>();
        using var subscription = RecordedSessionEditorEffects.CommandRefresh(states)
            .Subscribe(effects.Add);
        var telemetry = TestTelemetryData.CreateProcessed();
        var initial = CreateState(selectedPageIndex: 0);
        var loaded = initial with { TelemetryData = telemetry };

        states.OnNext(initial);
        states.OnNext(initial);
        states.OnNext(loaded);

        Assert.Collection(
            effects,
            effect => Assert.Null(Assert.IsType<RecordedSessionEditorEffect.RefreshCommands>(effect).LoadedData.TelemetryData),
            effect =>
            {
                var refresh = Assert.IsType<RecordedSessionEditorEffect.RefreshCommands>(effect);
                Assert.Same(telemetry, refresh.LoadedData.TelemetryData);
            });
    }

    [Fact]
    public void RecomputeStaleness_EmitsDistinctDomainChanges()
    {
        using var states = new Subject<RecordedSessionEditorState>();
        var effects = new List<RecordedSessionEditorEffect>();
        using var subscription = RecordedSessionEditorEffects.RecomputeStaleness(states)
            .Subscribe(effects.Add);
        var initial = CreateState(selectedPageIndex: 0);
        var domain = CreateDomain(TestSnapshots.Session(updated: 11), DerivedChangeKind.Initial);
        var nextDomain = CreateDomain(TestSnapshots.Session(updated: 12), DerivedChangeKind.FingerprintChanged);

        states.OnNext(initial);
        states.OnNext(initial with { Domain = domain });
        states.OnNext(initial with { Domain = domain });
        states.OnNext((initial with { Domain = domain }) with
        {
            Intent = initial.Intent with { SelectedPageIndex = 2 },
        });
        states.OnNext(initial with { Domain = nextDomain });

        Assert.Collection(
            effects,
            effect => Assert.Same(domain, Assert.IsType<RecordedSessionEditorEffect.EvaluateRecomputeStaleness>(effect).Domain),
            effect => Assert.Same(nextDomain, Assert.IsType<RecordedSessionEditorEffect.EvaluateRecomputeStaleness>(effect).Domain));
    }

    [Fact]
    public void DirtyBaselineTracking_EmitsForEveryChange()
    {
        using var changes = new Subject<Unit>();
        var effects = new List<RecordedSessionEditorEffect>();
        using var subscription = RecordedSessionEditorEffects.DirtyBaselineTracking(changes)
            .Subscribe(effects.Add);

        changes.OnNext(Unit.Default);
        changes.OnNext(Unit.Default);

        Assert.Collection(
            effects,
            effect => Assert.IsType<RecordedSessionEditorEffect.UpdateDirtyBaseline>(effect),
            effect => Assert.IsType<RecordedSessionEditorEffect.UpdateDirtyBaseline>(effect));
    }

    [Fact]
    public void PreferencePersistence_EmitsOnlyPreferenceIntents()
    {
        using var actions = new RecordedSessionEditorActions();
        var effects = new List<RecordedSessionEditorEffect>();
        using var subscription = RecordedSessionEditorEffects.PreferencePersistence(actions.Intents)
            .Subscribe(effects.Add);
        var signalDisplay = new SignalDisplayPreferences(Travel: false);

        actions.SetAnalysisRange(new TelemetryTimeRange(0, 1));
        actions.SetTravelDistributionMode(TravelDistributionMode.DynamicSag);
        actions.SetDampingSpeedCutoffs(DampingSpeedCutoffs.FromValues(110, 220, 330, 440));
        actions.SetSignalDisplayPreferences(signalDisplay);

        Assert.Collection(
            effects,
            effect =>
            {
                var persist = Assert.IsType<RecordedSessionEditorEffect.PersistPreferences>(effect);
                var intent = Assert.IsType<RecordedSessionEditorIntent.SetTravelDistributionMode>(persist.Intent);
                Assert.Equal(TravelDistributionMode.DynamicSag, intent.Mode);
            },
            effect =>
            {
                var persist = Assert.IsType<RecordedSessionEditorEffect.PersistPreferences>(effect);
                var intent = Assert.IsType<RecordedSessionEditorIntent.SetSignalDisplayPreferences>(persist.Intent);
                Assert.Equal(signalDisplay, intent.Preferences);
            });
    }

    [Fact]
    public void AnalysisRequests_EmitsOnlyAnalysisRequestIntents()
    {
        using var states = new Subject<RecordedSessionEditorState>();
        var effects = new List<RecordedSessionEditorEffect>();
        using var subscription = RecordedSessionEditorEffects.AnalysisRequests(states)
            .Subscribe(effects.Add);
        var initial = CreateState(selectedPageIndex: 0);
        var rangeChanged = initial with
        {
            Intent = initial.Intent with
            {
                AnalysisRange = new TelemetryTimeRange(0, 1),
            },
        };
        var travelChanged = rangeChanged with
        {
            Intent = rangeChanged.Intent with
            {
                SelectedTravelDistributionMode = TravelDistributionMode.DynamicSag,
            },
        };
        var velocityChanged = travelChanged with
        {
            Intent = travelChanged.Intent with
            {
                SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged,
            },
        };
        var cutoffsChanged = velocityChanged with
        {
            Intent = velocityChanged.Intent with
            {
                DampingSpeedCutoffs = DampingSpeedCutoffs.FromValues(110, 220, 330, 440),
            },
        };

        states.OnNext(initial);
        states.OnNext(initial with { Presentation = initial.Presentation });
        states.OnNext(rangeChanged);
        states.OnNext(travelChanged);
        states.OnNext(travelChanged);
        states.OnNext(velocityChanged);
        states.OnNext(cutoffsChanged);

        Assert.Collection(
            effects,
            effect =>
            {
                var request = Assert.IsType<RecordedSessionEditorEffect.RequestAnalysis>(effect);
                Assert.IsType<RecordedSessionAnalysisEffectRequest.RangeChanged>(request.Request);
            },
            effect =>
            {
                var request = Assert.IsType<RecordedSessionEditorEffect.RequestAnalysis>(effect);
                var insights = Assert.IsType<RecordedSessionAnalysisEffectRequest.Insights>(request.Request);
                Assert.True(insights.RespectSuppression);
            },
            effect =>
            {
                var request = Assert.IsType<RecordedSessionEditorEffect.RequestAnalysis>(effect);
                var damping = Assert.IsType<RecordedSessionAnalysisEffectRequest.Damping>(request.Request);
                Assert.True(damping.IncludeInsights);
                Assert.True(damping.RespectSuppression);
            },
            effect =>
            {
                var request = Assert.IsType<RecordedSessionEditorEffect.RequestAnalysis>(effect);
                var damping = Assert.IsType<RecordedSessionAnalysisEffectRequest.Damping>(request.Request);
                Assert.True(damping.IncludeInsights);
                Assert.True(damping.RespectSuppression);
            });
    }

    [Fact]
    public void PageSelectionAnalysisRequests_EmitsOnSelectedPageChanges()
    {
        using var states = new Subject<RecordedSessionEditorState>();
        var effects = new List<RecordedSessionEditorEffect>();
        using var subscription = RecordedSessionEditorEffects.PageSelectionAnalysisRequests(states)
            .Subscribe(effects.Add);

        states.OnNext(CreateState(selectedPageIndex: 0));
        states.OnNext(CreateState(selectedPageIndex: 0));
        states.OnNext(CreateState(selectedPageIndex: 1));
        states.OnNext(CreateState(selectedPageIndex: 1));
        states.OnNext(CreateState(selectedPageIndex: 2));

        Assert.Collection(
            effects,
            effect =>
            {
                var request = Assert.IsType<RecordedSessionEditorEffect.RequestAnalysis>(effect);
                Assert.IsType<RecordedSessionAnalysisEffectRequest.SelectedPageChanged>(request.Request);
            },
            effect =>
            {
                var request = Assert.IsType<RecordedSessionEditorEffect.RequestAnalysis>(effect);
                Assert.IsType<RecordedSessionAnalysisEffectRequest.SelectedPageChanged>(request.Request);
            });
    }

    [Fact]
    public void TelemetryAnalysisRequests_EmitsOnTelemetryChanges()
    {
        using var states = new Subject<RecordedSessionEditorState>();
        var effects = new List<RecordedSessionEditorEffect>();
        using var subscription = RecordedSessionEditorEffects.TelemetryAnalysisRequests(states)
            .Subscribe(effects.Add);
        var telemetry = TestTelemetryData.CreateProcessed();

        states.OnNext(CreateState(selectedPageIndex: 0));
        states.OnNext(CreateState(selectedPageIndex: 0));
        states.OnNext(CreateState(selectedPageIndex: 0, telemetry));
        states.OnNext(CreateState(selectedPageIndex: 1, telemetry));
        states.OnNext(CreateState(selectedPageIndex: 1));

        Assert.Collection(
            effects,
            effect =>
            {
                var request = Assert.IsType<RecordedSessionEditorEffect.RequestAnalysis>(effect);
                Assert.IsType<RecordedSessionAnalysisEffectRequest.TelemetryChanged>(request.Request);
            },
            effect =>
            {
                var request = Assert.IsType<RecordedSessionEditorEffect.RequestAnalysis>(effect);
                Assert.IsType<RecordedSessionAnalysisEffectRequest.TelemetryChanged>(request.Request);
            });
    }

    [Fact]
    public void ExplicitAnalysisRequests_EmitsUnsuppressedInsightsRequest()
    {
        using var actions = new RecordedSessionEditorActions();
        var effects = new List<RecordedSessionEditorEffect>();
        using var subscription = RecordedSessionEditorEffects.ExplicitAnalysisRequests(actions.Intents)
            .Subscribe(effects.Add);

        actions.SetTravelDistributionMode(TravelDistributionMode.DynamicSag);
        actions.RequestSessionInsights();
        actions.RequestDampingPercentages();
        actions.RefreshSelectedPageAnalysis();

        Assert.Collection(
            effects,
            effect =>
            {
                var request = Assert.IsType<RecordedSessionEditorEffect.RequestAnalysis>(effect);
                var insights = Assert.IsType<RecordedSessionAnalysisEffectRequest.Insights>(request.Request);
                Assert.False(insights.RespectSuppression);
            },
            effect =>
            {
                var request = Assert.IsType<RecordedSessionEditorEffect.RequestAnalysis>(effect);
                var damping = Assert.IsType<RecordedSessionAnalysisEffectRequest.Damping>(request.Request);
                Assert.False(damping.IncludeInsights);
                Assert.False(damping.RespectSuppression);
            },
            effect =>
            {
                var request = Assert.IsType<RecordedSessionEditorEffect.RequestAnalysis>(effect);
                Assert.IsType<RecordedSessionAnalysisEffectRequest.SelectedPageChanged>(request.Request);
            });
    }

    [Fact]
    public void StateController_DisposeStopsSourceSubscription()
    {
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var controller = driver.Controller;
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = controller.State.Subscribe(observed.Add);

        driver.PageCounts.OnNext(3);
        driver.Actions.SelectPageIndex(1);
        controller.Dispose();
        driver.Actions.SelectPageIndex(2);

        Assert.Collection(
            observed,
            state => Assert.Equal(0, state.Intent.SelectedPageIndex),
            state => Assert.Equal(1, state.Intent.SelectedPageIndex));
    }

    [Fact]
    public void Effects_AppliesEffects_AndDisposesSubscriptions()
    {
        using var source = new Subject<RecordedSessionEditorEffect>();
        var applied = new List<RecordedSessionEditorEffect>();
        var preferenceEffect = new RecordedSessionEditorEffect.PersistPreferences(
            new RecordedSessionEditorIntent.SetVelocityAverageMode(VelocityAverageMode.SampleAveraged));
        var commandEffect = new RecordedSessionEditorEffect.RefreshCommands(
            new RecordedSessionLoadedData(null, null, null, null, null));
        var effects = new RecordedSessionEditorEffects(source, applied.Add);

        source.OnNext(preferenceEffect);
        source.OnNext(preferenceEffect);
        source.OnNext(commandEffect);
        effects.Dispose();
        source.OnNext(new RecordedSessionEditorEffect.RefreshCommands(
            new RecordedSessionLoadedData(null, TestTelemetryData.CreateProcessed(), null, null, null)));

        Assert.Collection(
            applied,
            effect => Assert.Equal(preferenceEffect, effect),
            effect => Assert.Equal(preferenceEffect, effect),
            effect => Assert.Equal(commandEffect, effect));
    }

    private static List<RecordedSessionEditorIntent> Subscribe(RecordedSessionEditorActions actions)
    {
        var intents = new List<RecordedSessionEditorIntent>();
        actions.Intents.Subscribe(intents.Add);
        return intents;
    }

    private static List<T> AdjacentDistinct<T>(IEnumerable<T> values)
    {
        var distinct = new List<T>();
        foreach (var value in values)
        {
            if (distinct.Count == 0 ||
                !EqualityComparer<T>.Default.Equals(distinct[^1], value))
            {
                distinct.Add(value);
            }
        }

        return distinct;
    }

    private static RecordedSessionEditorState CreateState(int selectedPageIndex, TelemetryData? telemetryData = null)
    {
        var preferences = SessionPreferences.Default;

        return new RecordedSessionEditorState(
            Domain: null,
            Load: new RecordedSessionLoadPresentation.Empty(),
            Session: null,
            TelemetryData: telemetryData,
            FullTrackPoints: null,
            TrackPoints: null,
            TrackTimelineContext: null,
            Preferences: preferences,
            Intent: new RecordedSessionEditorIntentState(
                SelectedPageIndex: selectedPageIndex,
                AnalysisRange: null,
                PendingAnalysisRangeBoundary: null,
                SelectedTravelDistributionMode: TravelDistributionMode.ActiveSuspension,
                SelectedBalanceDisplacementMode: BalanceDisplacementMode.Zenith,
                SelectedBalanceSpeedMode: BalanceSpeedMode.Both,
                SelectedVelocityAverageMode: VelocityAverageMode.SampleAveraged,
                SelectedSessionInsightsTargetProfile: SessionInsightsTargetProfile.Trail,
                DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
                SignalDisplayPreferences: preferences.SignalDisplay,
                SignalLayoutPreferences: preferences.SignalLayout,
                LayoutPreferences: preferences.Layout),
            Presentation: new RecordedSessionEditorPresentationState(
                MapState: SurfacePresentationState.Hidden,
                MediaPaneState: SurfacePresentationState.Hidden,
                MediaColumnWidth: null,
                MediaUrl: null,
                SignalAvailability: new RecordedSignalAvailabilityState(
                    Travel: false,
                    Velocity: false,
                    Imu: false,
                    PitchRoll: false,
                    Speed: false,
                    Elevation: false),
                Signals: new RecordedSignalPresentationState(
                    Travel: SurfacePresentationState.Hidden,
                    Velocity: SurfacePresentationState.Hidden,
                    Imu: SurfacePresentationState.Hidden,
                    PitchRoll: SurfacePresentationState.Hidden,
                    Speed: SurfacePresentationState.Hidden,
                    Elevation: SurfacePresentationState.Hidden,
                    ShowAirtime: false,
                    ShowVelocityAirtime: false,
                    ShowImuAirtime: false,
                    ShowPitchRollAirtime: false,
                    ShowSpeedAirtime: false,
                    ShowElevationAirtime: false,
                    ShowAnalysisSelection: false,
                    ShowVelocityAnalysisSelection: false,
                    ShowImuAnalysisSelection: false,
                    ShowPitchRollAnalysisSelection: false,
                    ShowSpeedAnalysisSelection: false,
                    ShowElevationAnalysisSelection: false,
                    TravelHeaderActions: [],
                    VelocityHeaderActions: [],
                    ImuHeaderActions: [],
                    PitchRollHeaderActions: [],
                    SpeedHeaderActions: [],
                    ElevationHeaderActions: []),
                Analysis: new RecordedAnalysisPresentationState(
                    FrontAnalysis: SurfacePresentationState.Hidden,
                    RearAnalysis: SurfacePresentationState.Hidden,
                    CompressionBalance: SurfacePresentationState.Hidden,
                    ReboundBalance: SurfacePresentationState.Hidden,
                    FrontForkVibration: SurfacePresentationState.Hidden,
                    FrontFrameVibration: SurfacePresentationState.Hidden,
                    RearForkVibration: SurfacePresentationState.Hidden,
                    RearFrameVibration: SurfacePresentationState.Hidden),
                DampingPercentages: SessionDampingPercentages.Empty,
                PlotDampingSpeedCutoffs: DampingSpeedCutoffs.Default,
                CanEditDampingSpeedCutoffs: false,
                SessionInsights: SessionInsightsResult.Hidden,
                SignalPlotContextMenuActionsBySignalRowId: new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>(),
                ScreenState: SessionScreenPresentationState.Ready,
                OperationState: SessionOperationPresentationState.Hidden),
            AnalysisSelection: new AnalysisSelectionState(
                ActiveFront: null,
                ActiveRear: null,
                HighlightRanges: []));
    }

    private static RecordedSessionLoadPresentation.Loaded CreateLoadedPresentation(
        TelemetryData telemetry,
        List<TrackPoint>? fullTrackPoints = null,
        List<TrackPoint>? trackPoints = null,
        Guid? fullTrackId = null,
        double? mediaColumnWidth = null,
        SessionCachePresentationData? cache = null)
    {
        return new RecordedSessionLoadPresentation.Loaded(new SessionDetailData(
            new SessionTelemetryPresentationData(
                telemetry,
                fullTrackId,
                fullTrackPoints,
                trackPoints,
                mediaColumnWidth,
                SessionDampingPercentages.Empty),
            cache ?? new SessionCachePresentationData(
                FrontTravelDistribution: "front-travel",
                RearTravelDistribution: "rear-travel",
                FrontVelocityDistribution: "front-velocity",
                RearVelocityDistribution: "rear-velocity",
                CompressionBalance: "compression",
                ReboundBalance: "rebound",
                DampingPercentages: SessionDampingPercentages.Empty,
                BalanceAvailable: true)));
    }

    private static RecordedSessionDomainSnapshot CreateDomain(
        SessionSnapshot snapshot,
        DerivedChangeKind changeKind = DerivedChangeKind.None) => new(
        snapshot,
        null,
        null,
        null,
        null,
        null,
        null,
        new SessionStaleness.Current(),
        changeKind);
}

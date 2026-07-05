using System.Reactive.Subjects;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Presentation;
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
        using var source = new Subject<RecordedSessionEditorState>();
        using var controller = new RecordedSessionEditorStateController(source);
        var first = CreateState(selectedPageIndex: 1);
        var second = CreateState(selectedPageIndex: 2);
        var third = CreateState(selectedPageIndex: 3);

        source.OnNext(first);
        source.OnNext(second);
        source.OnNext(second);

        var observed = new List<RecordedSessionEditorState>();
        using var subscription = controller.State.Subscribe(observed.Add);

        source.OnNext(third);
        source.OnNext(third);

        Assert.Collection(
            observed,
            state => Assert.Equal(2, state.Intent.SelectedPageIndex),
            state => Assert.Equal(3, state.Intent.SelectedPageIndex));
    }

    [Fact]
    public void StateController_DerivesSelectedPageIndex_FromActionsAndPageCount()
    {
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts);
        var observed = new List<int>();
        using var subscription = controller.State.Subscribe(state => observed.Add(state.Intent.SelectedPageIndex));

        pageCounts.OnNext(3);
        legacyState.OnNext(CreateState(selectedPageIndex: 7));
        actions.SelectPageIndex(2);
        actions.SelectPageIndex(99);
        pageCounts.OnNext(2);
        actions.SelectPageIndex(-3);
        pageCounts.OnNext(0);

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
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var preferenceReplays = new Subject<SessionPreferences>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts,
            preferenceReplays);
        var observed = new List<RecordedSessionEditorIntentState>();
        using var subscription = controller.State.Subscribe(state => observed.Add(state.Intent));
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

        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        actions.SetTravelDistributionMode(TravelDistributionMode.DynamicSag);
        preferenceReplays.OnNext(replayedPreferences);
        actions.SetBalanceSpeedMode(BalanceSpeedMode.LowSpeed);
        actions.SetSignalDisplayPreferences(userSignalDisplay);
        actions.SetSignalLayoutPreferences(userSignalLayout);
        actions.SetLayoutPreferences(userLayout);
        var legacyOverride = CreateState(selectedPageIndex: 0);
        legacyState.OnNext(legacyOverride with
        {
            Intent = legacyOverride.Intent with
            {
                SelectedTravelDistributionMode = TravelDistributionMode.ActiveSuspension,
                SelectedBalanceSpeedMode = BalanceSpeedMode.Both,
                SignalDisplayPreferences = SessionPreferences.Default.SignalDisplay,
                SignalLayoutPreferences = SessionPreferences.Default.SignalLayout,
                LayoutPreferences = SessionPreferences.Default.Layout,
            },
            Presentation = legacyOverride.Presentation with
            {
                ScreenState = SessionScreenPresentationState.Loading("Reloading"),
            },
        });

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
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts);
        var observed = new List<TelemetryTimeRange?>();
        using var subscription = controller.State.Subscribe(state => observed.Add(state.Intent.AnalysisRange));
        var telemetry = TestTelemetryData.CreateMinimal(duration: 2.0);

        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        actions.SetAnalysisRange(new TelemetryTimeRange(1.0, 3.0));
        legacyState.OnNext(CreateState(selectedPageIndex: 0, telemetry));
        actions.ClearAnalysisRange();

        Assert.Collection(
            observed,
            range => Assert.Null(range),
            range =>
            {
                Assert.Equal(1.0, range?.StartSeconds);
                Assert.Equal(2.0, range?.EndSeconds);
            },
            range => Assert.Null(range));
    }

    [Fact]
    public void StateController_DerivesDampingSpeedCutoffs_FromActions()
    {
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts);
        var observed = new List<DampingSpeedCutoffs>();
        using var subscription = controller.State.Subscribe(state => observed.Add(state.Intent.DampingSpeedCutoffs));
        var cutoffs = DampingSpeedCutoffs.FromValues(110, 220, 330, 440);

        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        actions.SetDampingSpeedCutoffs(cutoffs);
        var staleLegacyState = CreateState(selectedPageIndex: 0);
        var legacyOverride = staleLegacyState with
        {
            Presentation = staleLegacyState.Presentation with
            {
                ScreenState = SessionScreenPresentationState.Loading("Reloading"),
            },
        };
        legacyState.OnNext(legacyOverride);

        Assert.Collection(
            observed,
            value => Assert.Equal(DampingSpeedCutoffs.Default, value),
            value => Assert.Equal(cutoffs, value));
    }

    [Fact]
    public void StateController_DerivesScreenAndOperationState_FromLifecycleInputs()
    {
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var preferenceReplays = new Subject<SessionPreferences>();
        using var screenStates = new Subject<SessionScreenPresentationState>();
        using var operationStates = new Subject<SessionOperationPresentationState>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates);
        var observed = new List<RecordedSessionEditorPresentationState>();
        using var subscription = controller.State.Subscribe(state => observed.Add(state.Presentation));
        var loading = SessionScreenPresentationState.Loading("Loading");
        var operation = SessionOperationPresentationState.Progress("Working", 25);

        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        screenStates.OnNext(loading);
        operationStates.OnNext(operation);
        var staleLegacyBaseState = CreateState(selectedPageIndex: 0);
        var staleLegacyState = staleLegacyBaseState with
        {
            Presentation = staleLegacyBaseState.Presentation with
            {
                ScreenState = SessionScreenPresentationState.Error("Stale error"),
                OperationState = SessionOperationPresentationState.Hidden,
            },
        };
        legacyState.OnNext(staleLegacyState);

        Assert.Collection(
            observed,
            state =>
            {
                Assert.Equal(SessionScreenPresentationState.Ready, state.ScreenState);
                Assert.Equal(SessionOperationPresentationState.Hidden, state.OperationState);
            },
            state =>
            {
                Assert.Equal(loading, state.ScreenState);
                Assert.Equal(SessionOperationPresentationState.Hidden, state.OperationState);
            },
            state =>
            {
                Assert.Equal(loading, state.ScreenState);
                Assert.Equal(operation, state.OperationState);
            });
    }

    [Fact]
    public void StateController_DerivesMediaPresentationState_FromMediaInputs()
    {
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var preferenceReplays = new Subject<SessionPreferences>();
        using var screenStates = new Subject<SessionScreenPresentationState>();
        using var operationStates = new Subject<SessionOperationPresentationState>();
        using var mapStates = new Subject<SurfacePresentationState>();
        using var mediaPaneStates = new Subject<SurfacePresentationState>();
        using var mediaColumnWidths = new Subject<double?>();
        using var mediaUrls = new Subject<string?>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates,
            mapStates,
            mediaPaneStates,
            mediaColumnWidths,
            mediaUrls);
        var observed = new List<RecordedSessionEditorPresentationState>();
        using var subscription = controller.State.Subscribe(state => observed.Add(state.Presentation));
        const double mediaColumnWidth = 480;
        const string mediaUrl = "session-media.mp4";

        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        mapStates.OnNext(SurfacePresentationState.Ready);
        mediaPaneStates.OnNext(SurfacePresentationState.Ready);
        mediaColumnWidths.OnNext(mediaColumnWidth);
        mediaUrls.OnNext(mediaUrl);
        var staleLegacyBaseState = CreateState(selectedPageIndex: 0);
        var staleLegacyState = staleLegacyBaseState with
        {
            Presentation = staleLegacyBaseState.Presentation with
            {
                MapState = SurfacePresentationState.Hidden,
                MediaPaneState = SurfacePresentationState.Hidden,
                MediaColumnWidth = 123,
                MediaUrl = "stale.mp4",
            },
        };
        legacyState.OnNext(staleLegacyState);

        Assert.Equal(5, observed.Count);
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
    public void StateController_DerivesAnalysisPresentationState_FromAnalysisInputs()
    {
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var preferenceReplays = new Subject<SessionPreferences>();
        using var screenStates = new Subject<SessionScreenPresentationState>();
        using var operationStates = new Subject<SessionOperationPresentationState>();
        using var mapStates = new Subject<SurfacePresentationState>();
        using var mediaPaneStates = new Subject<SurfacePresentationState>();
        using var mediaColumnWidths = new Subject<double?>();
        using var mediaUrls = new Subject<string?>();
        using var analysisPresentationStates = new Subject<RecordedAnalysisPresentationState>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates,
            mapStates,
            mediaPaneStates,
            mediaColumnWidths,
            mediaUrls,
            analysisPresentationStates);
        var observed = new List<RecordedAnalysisPresentationState>();
        using var subscription = controller.State.Subscribe(state => observed.Add(state.Presentation.Analysis));
        var analysis = new RecordedAnalysisPresentationState(
            FrontAnalysis: SurfacePresentationState.Ready,
            RearAnalysis: SurfacePresentationState.Loading("Rear"),
            CompressionBalance: SurfacePresentationState.Ready,
            ReboundBalance: SurfacePresentationState.Hidden,
            FrontForkVibration: SurfacePresentationState.Ready,
            FrontFrameVibration: SurfacePresentationState.Hidden,
            RearForkVibration: SurfacePresentationState.Ready,
            RearFrameVibration: SurfacePresentationState.Hidden);

        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        analysisPresentationStates.OnNext(analysis);
        var staleLegacyBaseState = CreateState(selectedPageIndex: 0);
        var staleLegacyState = staleLegacyBaseState with
        {
            Presentation = staleLegacyBaseState.Presentation with
            {
                Analysis = staleLegacyBaseState.Presentation.Analysis with
                {
                    FrontAnalysis = SurfacePresentationState.Error("Stale"),
                },
            },
        };
        legacyState.OnNext(staleLegacyState);

        Assert.Equal(2, observed.Count);
        Assert.True(observed[0].FrontAnalysis.IsHidden);
        Assert.Equal(analysis, observed[1]);
        Assert.Equal(analysis, observed[^1]);
    }

    [Fact]
    public void StateController_DerivesAnalysisPresentationDetails_FromInputs()
    {
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var preferenceReplays = new Subject<SessionPreferences>();
        using var screenStates = new Subject<SessionScreenPresentationState>();
        using var operationStates = new Subject<SessionOperationPresentationState>();
        using var mapStates = new Subject<SurfacePresentationState>();
        using var mediaPaneStates = new Subject<SurfacePresentationState>();
        using var mediaColumnWidths = new Subject<double?>();
        using var mediaUrls = new Subject<string?>();
        using var analysisPresentationStates = new Subject<RecordedAnalysisPresentationState>();
        using var dampingPercentages = new Subject<SessionDampingPercentages>();
        using var plotDampingSpeedCutoffs = new Subject<DampingSpeedCutoffs>();
        using var canEditDampingSpeedCutoffs = new Subject<bool>();
        using var sessionInsights = new Subject<SessionInsightsResult>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates,
            mapStates,
            mediaPaneStates,
            mediaColumnWidths,
            mediaUrls,
            analysisPresentationStates,
            dampingPercentages,
            plotDampingSpeedCutoffs,
            canEditDampingSpeedCutoffs,
            sessionInsights);
        var observed = new List<RecordedSessionEditorPresentationState>();
        using var subscription = controller.State.Subscribe(state => observed.Add(state.Presentation));
        var percentages = new SessionDampingPercentages(1, 2, 3, 4, 5, 6, 7, 8);
        var plotCutoffs = DampingSpeedCutoffs.FromValues(120, 240, 360, 480);
        var insights = new SessionInsightsResult(SurfacePresentationState.Loading("Insights"), []);

        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        dampingPercentages.OnNext(percentages);
        plotDampingSpeedCutoffs.OnNext(plotCutoffs);
        canEditDampingSpeedCutoffs.OnNext(true);
        sessionInsights.OnNext(insights);
        var staleLegacyBaseState = CreateState(selectedPageIndex: 0);
        var staleLegacyState = staleLegacyBaseState with
        {
            Presentation = staleLegacyBaseState.Presentation with
            {
                DampingPercentages = SessionDampingPercentages.Empty,
                PlotDampingSpeedCutoffs = DampingSpeedCutoffs.Default,
                CanEditDampingSpeedCutoffs = false,
                SessionInsights = SessionInsightsResult.Hidden,
            },
        };
        legacyState.OnNext(staleLegacyState);

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
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var preferenceReplays = new Subject<SessionPreferences>();
        using var screenStates = new Subject<SessionScreenPresentationState>();
        using var operationStates = new Subject<SessionOperationPresentationState>();
        using var mapStates = new Subject<SurfacePresentationState>();
        using var mediaPaneStates = new Subject<SurfacePresentationState>();
        using var mediaColumnWidths = new Subject<double?>();
        using var mediaUrls = new Subject<string?>();
        using var analysisPresentationStates = new Subject<RecordedAnalysisPresentationState>();
        using var dampingPercentages = new Subject<SessionDampingPercentages>();
        using var plotDampingSpeedCutoffs = new Subject<DampingSpeedCutoffs>();
        using var canEditDampingSpeedCutoffs = new Subject<bool>();
        using var sessionInsights = new Subject<SessionInsightsResult>();
        using var signalPresentationStates = new Subject<RecordedSignalPresentationState>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates,
            mapStates,
            mediaPaneStates,
            mediaColumnWidths,
            mediaUrls,
            analysisPresentationStates,
            dampingPercentages,
            plotDampingSpeedCutoffs,
            canEditDampingSpeedCutoffs,
            sessionInsights,
            signalPresentationStates);
        var observed = new List<RecordedSignalPresentationState>();
        using var subscription = controller.State.Subscribe(state => observed.Add(state.Presentation.Signals));
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

        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        signalPresentationStates.OnNext(signals);
        var staleLegacyState = CreateState(selectedPageIndex: 0, telemetryData: TestTelemetryData.CreateProcessed());
        legacyState.OnNext(staleLegacyState);

        Assert.Equal(3, observed.Count);
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
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var preferenceReplays = new Subject<SessionPreferences>();
        using var screenStates = new Subject<SessionScreenPresentationState>();
        using var operationStates = new Subject<SessionOperationPresentationState>();
        using var mapStates = new Subject<SurfacePresentationState>();
        using var mediaPaneStates = new Subject<SurfacePresentationState>();
        using var mediaColumnWidths = new Subject<double?>();
        using var mediaUrls = new Subject<string?>();
        using var analysisPresentationStates = new Subject<RecordedAnalysisPresentationState>();
        using var dampingPercentages = new Subject<SessionDampingPercentages>();
        using var plotDampingSpeedCutoffs = new Subject<DampingSpeedCutoffs>();
        using var canEditDampingSpeedCutoffs = new Subject<bool>();
        using var sessionInsights = new Subject<SessionInsightsResult>();
        using var signalPresentationStates = new Subject<RecordedSignalPresentationState>();
        using var analysisSelectionStates = new Subject<AnalysisSelectionState>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates,
            mapStates,
            mediaPaneStates,
            mediaColumnWidths,
            mediaUrls,
            analysisPresentationStates,
            dampingPercentages,
            plotDampingSpeedCutoffs,
            canEditDampingSpeedCutoffs,
            sessionInsights,
            signalPresentationStates,
            analysisSelectionStates);
        var observed = new List<AnalysisSelectionState>();
        using var subscription = controller.State.Subscribe(state => observed.Add(state.AnalysisSelection));
        var selection = new DeepTravelRangeSelection(
            SuspensionType.Front,
            new TelemetryRangeSelection.BinRange(0, 0, 10, IsFirst: true, IsLast: false));
        TelemetryHighlightRange[] highlightRanges = [new TelemetryHighlightRange(1, 2, SuspensionType.Front)];
        var analysisSelection = new AnalysisSelectionState(
            ActiveFront: selection,
            ActiveRear: null,
            HighlightRanges: highlightRanges);

        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        analysisSelectionStates.OnNext(analysisSelection);
        var staleLegacyState = CreateState(selectedPageIndex: 0, telemetryData: TestTelemetryData.CreateProcessed());
        legacyState.OnNext(staleLegacyState);

        Assert.Equal(3, observed.Count);
        Assert.Null(observed[0].ActiveFront);
        Assert.Empty(observed[0].HighlightRanges);
        Assert.Equal(analysisSelection, observed[1]);
        Assert.Equal(analysisSelection, observed[^1]);
        Assert.Same(highlightRanges, observed[^1].HighlightRanges);
    }

    [Fact]
    public void StateController_DerivesLoadedData_FromInputs()
    {
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var preferenceReplays = new Subject<SessionPreferences>();
        using var screenStates = new Subject<SessionScreenPresentationState>();
        using var operationStates = new Subject<SessionOperationPresentationState>();
        using var mapStates = new Subject<SurfacePresentationState>();
        using var mediaPaneStates = new Subject<SurfacePresentationState>();
        using var mediaColumnWidths = new Subject<double?>();
        using var mediaUrls = new Subject<string?>();
        using var analysisPresentationStates = new Subject<RecordedAnalysisPresentationState>();
        using var dampingPercentages = new Subject<SessionDampingPercentages>();
        using var plotDampingSpeedCutoffs = new Subject<DampingSpeedCutoffs>();
        using var canEditDampingSpeedCutoffs = new Subject<bool>();
        using var sessionInsights = new Subject<SessionInsightsResult>();
        using var signalPresentationStates = new Subject<RecordedSignalPresentationState>();
        using var analysisSelectionStates = new Subject<AnalysisSelectionState>();
        using var loadedDataStates = new Subject<RecordedSessionLoadedData>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts,
            preferenceReplays,
            screenStates,
            operationStates,
            mapStates,
            mediaPaneStates,
            mediaColumnWidths,
            mediaUrls,
            analysisPresentationStates,
            dampingPercentages,
            plotDampingSpeedCutoffs,
            canEditDampingSpeedCutoffs,
            sessionInsights,
            signalPresentationStates,
            analysisSelectionStates,
            loadedDataStates);
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = controller.State.Subscribe(observed.Add);
        var session = TestSnapshots.Session();
        var telemetry = TestTelemetryData.CreateProcessed();
        List<TrackPoint> fullTrackPoints = [new TrackPoint(1, 2, 3, 4)];
        List<TrackPoint> trackPoints = [new TrackPoint(5, 6, 7, 8)];
        var timelineContext = new TrackTimeRange(10, 20);
        var loadedData = new RecordedSessionLoadedData(
            session,
            telemetry,
            fullTrackPoints,
            trackPoints,
            timelineContext);

        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        loadedDataStates.OnNext(loadedData);
        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        actions.SetTravelDistributionMode(TravelDistributionMode.DynamicSag);

        Assert.Null(observed[0].Session);
        Assert.Null(observed[0].TelemetryData);
        Assert.Same(session, observed[1].Session);
        Assert.Same(telemetry, observed[1].TelemetryData);
        Assert.Same(fullTrackPoints, observed[1].FullTrackPoints);
        Assert.Same(trackPoints, observed[1].TrackPoints);
        Assert.Equal(timelineContext, observed[1].TrackTimelineContext);
        Assert.Same(session, observed[^1].Session);
        Assert.Same(telemetry, observed[^1].TelemetryData);
        Assert.Same(fullTrackPoints, observed[^1].FullTrackPoints);
        Assert.Same(trackPoints, observed[^1].TrackPoints);
        Assert.Equal(timelineContext, observed[^1].TrackTimelineContext);
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
        using var source = new Subject<RecordedSessionEditorState>();
        var controller = new RecordedSessionEditorStateController(source);
        var observed = new List<RecordedSessionEditorState>();
        using var subscription = controller.State.Subscribe(observed.Add);

        source.OnNext(CreateState(selectedPageIndex: 1));
        controller.Dispose();
        source.OnNext(CreateState(selectedPageIndex: 2));

        Assert.Collection(
            observed,
            state => Assert.Equal(1, state.Intent.SelectedPageIndex));
    }

    [Fact]
    public void Effects_AppliesEffects_AndDisposesSubscriptions()
    {
        using var source = new Subject<RecordedSessionEditorEffect>();
        var applied = new List<RecordedSessionEditorEffect>();
        var preferenceEffect = new RecordedSessionEditorEffect.PersistPreferences(
            new RecordedSessionEditorIntent.SetVelocityAverageMode(VelocityAverageMode.SampleAveraged));
        var commandEffect = new RecordedSessionEditorEffect.RefreshCommands(CreateState(selectedPageIndex: 1));
        var effects = new RecordedSessionEditorEffects(source, applied.Add);

        source.OnNext(preferenceEffect);
        source.OnNext(preferenceEffect);
        source.OnNext(commandEffect);
        effects.Dispose();
        source.OnNext(new RecordedSessionEditorEffect.RefreshCommands(CreateState(selectedPageIndex: 2)));

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

    private static RecordedSessionEditorState CreateState(int selectedPageIndex, TelemetryData? telemetryData = null)
    {
        var preferences = SessionPreferences.Default;

        return new RecordedSessionEditorState(
            Domain: null,
            Session: null,
            TelemetryData: telemetryData,
            FullTrackPoints: null,
            TrackPoints: null,
            TrackTimelineContext: null,
            Preferences: preferences,
            Intent: new RecordedSessionEditorIntentState(
                SelectedPageIndex: selectedPageIndex,
                AnalysisRange: null,
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
}

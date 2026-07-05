using System.Reactive.Subjects;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
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
    public void StateController_DerivesAnalysisModes_FromActionsAndPreferenceReplay()
    {
        using var legacyState = new Subject<RecordedSessionEditorState>();
        using var actions = new RecordedSessionEditorActions();
        using var pageCounts = new Subject<int>();
        using var preferenceReplays = new Subject<AnalysisPreferences>();
        using var controller = new RecordedSessionEditorStateController(
            legacyState,
            actions.Intents,
            pageCounts,
            preferenceReplays);
        var observed = new List<RecordedSessionEditorIntentState>();
        using var subscription = controller.State.Subscribe(state => observed.Add(state.Intent));
        var replayedPreferences = new AnalysisPreferences(
            TravelDistributionMode.DynamicSag,
            VelocityAverageMode.StrokePeakAveraged,
            BalanceDisplacementMode.Travel,
            BalanceSpeedMode.HighSpeed,
            SessionInsightsTargetProfile.DH);

        legacyState.OnNext(CreateState(selectedPageIndex: 0));
        actions.SetTravelDistributionMode(TravelDistributionMode.DynamicSag);
        preferenceReplays.OnNext(replayedPreferences);
        actions.SetBalanceSpeedMode(BalanceSpeedMode.LowSpeed);
        var legacyOverride = CreateState(selectedPageIndex: 0);
        legacyState.OnNext(legacyOverride with
        {
            Intent = legacyOverride.Intent with
            {
                SelectedTravelDistributionMode = TravelDistributionMode.ActiveSuspension,
                SelectedBalanceSpeedMode = BalanceSpeedMode.Both,
            },
            Presentation = legacyOverride.Presentation with
            {
                ScreenState = SessionScreenPresentationState.Loading("Reloading"),
            },
        });

        Assert.Equal(5, observed.Count);
        Assert.Equal(TravelDistributionMode.ActiveSuspension, observed[0].SelectedTravelDistributionMode);
        Assert.Equal(TravelDistributionMode.DynamicSag, observed[1].SelectedTravelDistributionMode);
        Assert.Equal(replayedPreferences.TravelDistributionMode, observed[2].SelectedTravelDistributionMode);
        Assert.Equal(replayedPreferences.VelocityAverageMode, observed[2].SelectedVelocityAverageMode);
        Assert.Equal(replayedPreferences.BalanceDisplacementMode, observed[2].SelectedBalanceDisplacementMode);
        Assert.Equal(replayedPreferences.BalanceSpeedMode, observed[2].SelectedBalanceSpeedMode);
        Assert.Equal(replayedPreferences.SessionInsightsTargetProfile, observed[2].SelectedSessionInsightsTargetProfile);
        Assert.Equal(BalanceSpeedMode.LowSpeed, observed[3].SelectedBalanceSpeedMode);
        Assert.Equal(TravelDistributionMode.DynamicSag, observed[^1].SelectedTravelDistributionMode);
        Assert.Equal(BalanceSpeedMode.LowSpeed, observed[^1].SelectedBalanceSpeedMode);
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
    public void Effects_AppliesDistinctEffects_AndDisposesSubscriptions()
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

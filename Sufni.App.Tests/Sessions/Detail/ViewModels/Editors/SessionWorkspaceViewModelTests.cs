using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.Acquisition.Models;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Analysis.ViewModels.Editors;
using Sufni.App.Shared.Base;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Media.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class SessionWorkspaceViewModelTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> EmptySignalPlotContextMenuActionsBySignalRowId =
        new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>();

    [Fact]
    public void RecordedSessionSignalsWorkspace_ForwardsActionsAndContextChanges()
    {
        var sourceVisibility = new TelemetrySourceVisibilityStore();
        var timeline = new SessionTimelineLinkViewModel();
        var extensionSlots = new RecordedSessionExtensionSlots();
        using var actions = new RecordedSessionEditorActions();
        var intents = Subscribe(actions);
        using var state = new Subject<RecordedSessionEditorState>();
        var workspace = new RecordedSessionSignalsWorkspaceViewModel(
            state,
            sourceVisibility,
            timeline,
            () => extensionSlots,
            actions);
        var changes = TrackPropertyChanges(workspace);
        state.OnNext(CreateState());

        workspace.SignalLayoutPreferences = SessionPreferences.Default.SignalLayout;
        workspace.SetAnalysisRange(1.25, 3.5);
        workspace.ClearAnalysisRange();
        workspace.SetAnalysisRangeBoundary(2.25);
        state.OnNext(CreateState(travelSignalState: SurfacePresentationState.Ready));

        Assert.Collection(
            intents,
            intent => Assert.Equal(SessionPreferences.Default.SignalLayout, Assert.IsType<RecordedSessionEditorIntent.SetSignalLayoutPreferences>(intent).Preferences),
            intent => Assert.Equal(new TelemetryTimeRange(1.25, 3.5), Assert.IsType<RecordedSessionEditorIntent.SetAnalysisRange>(intent).Range),
            intent => Assert.IsType<RecordedSessionEditorIntent.ClearAnalysisRange>(intent),
            intent => Assert.Equal(2.25, Assert.IsType<RecordedSessionEditorIntent.SetAnalysisRangeBoundary>(intent).Seconds));
        Assert.Equal(SurfacePresentationState.Ready, workspace.TravelSignalState);
        Assert.Contains(nameof(RecordedSessionSignalsWorkspaceViewModel.TravelSignalState), changes);
    }

    [Fact]
    public void SessionAnalysisWorkspace_ModeSetters_EmitActions()
    {
        var (_, _, actions, workspace) = CreateAnalysisWorkspace();
        var intents = Subscribe(actions);

        workspace.SelectedTravelDistributionMode = TravelDistributionMode.DynamicSag;
        workspace.SelectedBalanceDisplacementMode = BalanceDisplacementMode.Speed;
        workspace.SelectedBalanceSpeedMode = BalanceSpeedMode.HighSpeed;
        workspace.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;
        workspace.SelectedSessionInsightsTargetProfile = SessionInsightsTargetProfile.Enduro;

        Assert.Collection(
            intents,
            intent => Assert.Equal(TravelDistributionMode.DynamicSag, Assert.IsType<RecordedSessionEditorIntent.SetTravelDistributionMode>(intent).Mode),
            intent => Assert.Equal(BalanceDisplacementMode.Speed, Assert.IsType<RecordedSessionEditorIntent.SetBalanceDisplacementMode>(intent).Mode),
            intent => Assert.Equal(BalanceSpeedMode.HighSpeed, Assert.IsType<RecordedSessionEditorIntent.SetBalanceSpeedMode>(intent).Mode),
            intent => Assert.Equal(VelocityAverageMode.StrokePeakAveraged, Assert.IsType<RecordedSessionEditorIntent.SetVelocityAverageMode>(intent).Mode),
            intent => Assert.Equal(SessionInsightsTargetProfile.Enduro, Assert.IsType<RecordedSessionEditorIntent.SetSessionInsightsTargetProfile>(intent).Profile));
    }

    [Fact]
    public async Task SessionAnalysisWorkspace_DampingCallbacks_RouteThroughTheGateway()
    {
        var (_, gateway, _, workspace) = CreateAnalysisWorkspace();

        workspace.PreviewDampingSpeedCutoff(SuspensionType.Front, DampingSpeedCircuit.Compression, 123);
        workspace.CancelDampingSpeedCutoffPreview();
        await workspace.CommitDampingSpeedCutoffAsync(SuspensionType.Rear, DampingSpeedCircuit.Rebound, 321);

        Assert.Equal((SuspensionType.Front, DampingSpeedCircuit.Compression, 123), Assert.Single(gateway.CutoffPreviews));
        Assert.Equal(1, gateway.CutoffPreviewCancellations);
        Assert.Equal((SuspensionType.Rear, DampingSpeedCircuit.Rebound, 321), Assert.Single(gateway.CutoffCommits));
    }

    [Fact]
    public void SessionAnalysisWorkspace_AnalysisTexts_TrackContextChanges()
    {
        var (state, _, _, workspace) = CreateAnalysisWorkspace();
        var changes = TrackPropertyChanges(workspace);

        state.OnNext(CreateState(
            analysisRange: new TelemetryTimeRange(1, 3),
            selectedBalanceSpeedMode: BalanceSpeedMode.LowSpeed));

        Assert.Contains("1.0", workspace.SessionAnalysisRangeText, StringComparison.Ordinal);
        Assert.Contains("3.0", workspace.SessionAnalysisRangeText, StringComparison.Ordinal);
        Assert.Contains(nameof(SessionAnalysisWorkspaceViewModel.SessionAnalysisRangeText), changes);
        Assert.Contains(nameof(SessionAnalysisWorkspaceViewModel.SessionAnalysisModesText), changes);
    }

    [Fact]
    public void SessionShellMobileWorkspace_SelectedPageState_ComesFromEditorState()
    {
        var pages = new ObservableCollection<PageViewModelBase>();
        using var driver = new RecordedSessionEditorStateControllerTestDriver();
        var workspace = new SessionShellMobileWorkspaceViewModel(
            new TestTabPageViewModel(new InlineUiThreadDispatcher()),
            pages,
            driver.Controller.State,
            driver.Actions);
        var signals = new PageViewModelBase("Signals");
        var damping = new PageViewModelBase("Damping");
        var changes = TrackPropertyChanges(workspace);

        pages.Add(signals);
        driver.PageCounts.OnNext(pages.Count);
        pages.Add(damping);
        driver.PageCounts.OnNext(pages.Count);

        Assert.Equal(2, workspace.PageCount);
        Assert.Equal(0, workspace.SelectedPageIndex);
        Assert.Same(signals, workspace.SelectedPage);
        Assert.Equal("Signals", workspace.SelectedPageDisplayName);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.PageCount), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SelectedPage), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SelectedPageDisplayName), changes);

        changes.Clear();
        workspace.SelectedPageIndex = 1;

        Assert.Equal(1, workspace.SelectedPageIndex);
        Assert.Same(damping, workspace.SelectedPage);
        Assert.Equal("Damping", workspace.SelectedPageDisplayName);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SelectedPageIndex), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SelectedPage), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SelectedPageDisplayName), changes);

        workspace.SelectedPageIndex = 99;
        Assert.Equal(1, workspace.SelectedPageIndex);

        workspace.SelectedPageIndex = -1;
        Assert.Equal(0, workspace.SelectedPageIndex);

        workspace.SelectedPageIndex = 1;
        changes.Clear();

        pages.Remove(damping);
        driver.PageCounts.OnNext(pages.Count);

        Assert.Equal(0, workspace.SelectedPageIndex);
        Assert.Same(signals, workspace.SelectedPage);
        Assert.Equal("Signals", workspace.SelectedPageDisplayName);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SelectedPageIndex), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.PageCount), changes);

        changes.Clear();

        pages.Clear();
        driver.PageCounts.OnNext(pages.Count);

        Assert.Equal(0, workspace.SelectedPageIndex);
        Assert.Null(workspace.SelectedPage);
        Assert.Equal(0, workspace.PageCount);
        Assert.Equal(string.Empty, workspace.SelectedPageDisplayName);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SelectedPage), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.PageCount), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SelectedPageDisplayName), changes);
    }

    private static (Subject<RecordedSessionEditorState> State, TestSessionOperationGateway Gateway, RecordedSessionEditorActions Actions, SessionAnalysisWorkspaceViewModel Workspace) CreateAnalysisWorkspace()
    {
        var extensionSlots = new RecordedSessionExtensionSlots();
        var gateway = new TestSessionOperationGateway();
        var actions = new RecordedSessionEditorActions();
        var state = new Subject<RecordedSessionEditorState>();
        var workspace = new SessionAnalysisWorkspaceViewModel(
            state,
            () => extensionSlots,
            gateway,
            actions,
            new RelayCommand<TelemetryRangeSelection?>(_ => { }),
            Substitute.For<IRecordedSessionAnalysisResultState>());
        state.OnNext(CreateState());
        return (state, gateway, actions, workspace);
    }

    [Fact]
    public void SessionMediaWorkspace_TracksSurfaceStateAndExtensionMediaPanes()
    {
        var timeline = new SessionTimelineLinkViewModel();
        var extensionSlots = new RecordedSessionExtensionSlots();
        using var state = new Subject<RecordedSessionEditorState>();
        var workspace = new SessionMediaWorkspaceViewModel(
            state,
            () => null,
            timeline,
            () => extensionSlots);
        var changes = TrackPropertyChanges(workspace);
        state.OnNext(CreateState());

        Assert.False(workspace.HasMediaContent);

        state.OnNext(CreateState(mapState: SurfacePresentationState.Ready));

        Assert.True(workspace.HasMediaContent);
        Assert.Contains(nameof(SessionMediaWorkspaceViewModel.MapState), changes);
        Assert.Contains(nameof(SessionMediaWorkspaceViewModel.HasMediaContent), changes);

        changes.Clear();
        state.OnNext(CreateState(mapState: SurfacePresentationState.Hidden));
        extensionSlots.MediaPanes.Add(new RecordedSessionMediaPaneContribution(
            "extension",
            "media",
            Order: 0,
            Substitute.For<IRecordedSessionMediaPaneContributionViewModel>()));

        Assert.True(workspace.HasMediaContent);
        Assert.Contains(nameof(SessionMediaWorkspaceViewModel.HasMediaContent), changes);
    }

    [Fact]
    public void SessionShellMobileWorkspace_ForwardsPresentationStateChanges()
    {
        var pages = new ObservableCollection<PageViewModelBase>();
        using var actions = new RecordedSessionEditorActions();
        using var state = new Subject<RecordedSessionEditorState>();
        var intents = Subscribe(actions);
        var workspace = new SessionShellMobileWorkspaceViewModel(
            new TestTabPageViewModel(new InlineUiThreadDispatcher()),
            pages,
            state,
            actions);
        var changes = TrackPropertyChanges(workspace);

        var screenState = SessionScreenPresentationState.Loading("Loading session.");
        var operationState = SessionOperationPresentationState.Progress("Saving.", 25);
        pages.Add(new PageViewModelBase("Signals"));
        pages.Add(new PageViewModelBase("Damping"));
        state.OnNext(CreateState(
            selectedPageIndex: 1,
            screenState: screenState,
            operationState: operationState));

        Assert.Equal(screenState, workspace.ScreenState);
        Assert.Equal(operationState, workspace.SessionOperationState);
        Assert.Equal(1, workspace.SelectedPageIndex);
        Assert.Same(pages[1], workspace.SelectedPage);
        Assert.Equal(2, workspace.PageCount);
        Assert.Equal("Damping", workspace.SelectedPageDisplayName);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.ScreenState), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SessionOperationState), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SelectedPageIndex), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SelectedPage), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.PageCount), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SelectedPageDisplayName), changes);

        workspace.SelectedPageIndex = 0;

        var intent = Assert.IsType<RecordedSessionEditorIntent.SelectPageIndex>(Assert.Single(intents));
        Assert.Equal(0, intent.PageIndex);
    }

    [Fact]
    public void SessionSidebarWorkspace_ForwardsNameDescriptionAndCommands()
    {
        var owner = new TestOwner();
        var name = "Original";
        var notesPage = new NotesPageViewModel();
        var preferencesPage = new PreferencesPageViewModel();
        var saveCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        var resetCommand = new AsyncRelayCommand(() => Task.CompletedTask);
        var workspace = new SessionSidebarWorkspaceViewModel(
            owner,
            () => name,
            value => name = value,
            notesPage,
            preferencesPage,
            saveCommand,
            resetCommand);
        var changes = TrackPropertyChanges(workspace);

        workspace.Name = "Updated";
        workspace.DescriptionText = "Draft notes";

        Assert.Equal("Updated", name);
        Assert.Equal("Draft notes", notesPage.Description);
        Assert.Same(notesPage, workspace.NotesPage);
        Assert.Same(preferencesPage, workspace.PreferencesPage);
        Assert.Same(saveCommand, workspace.SaveCommand);
        Assert.Same(resetCommand, workspace.ResetCommand);
        Assert.Contains(nameof(SessionSidebarWorkspaceViewModel.Name), changes);
        Assert.Contains(nameof(SessionSidebarWorkspaceViewModel.DescriptionText), changes);

        changes.Clear();
        owner.RaiseNameChanged();
        notesPage.Description = "External notes";

        Assert.Equal("External notes", workspace.DescriptionText);
        Assert.Contains(nameof(SessionSidebarWorkspaceViewModel.Name), changes);
        Assert.Contains(nameof(SessionSidebarWorkspaceViewModel.DescriptionText), changes);
    }

    [Fact]
    public void SignalsWorkspace_ForwardedPropertiesAreDeclaredPublicProperties() =>
        AssertForwardedPropertiesAreDeclared(
            typeof(RecordedSessionSignalsWorkspaceViewModel),
            RecordedSessionSignalsWorkspaceViewModel.ForwardedProperties);

    [Fact]
    public void AnalysisWorkspace_ForwardedPropertiesAreDeclaredPublicProperties() =>
        AssertForwardedPropertiesAreDeclared(
            typeof(SessionAnalysisWorkspaceViewModel),
            SessionAnalysisWorkspaceViewModel.ForwardedProperties);

    [Fact]
    public void SignalsWorkspace_DoesNotRebroadcastUndeclaredContextProperties()
    {
        var sourceVisibility = new TelemetrySourceVisibilityStore();
        var timeline = new SessionTimelineLinkViewModel();
        var extensionSlots = new RecordedSessionExtensionSlots();
        using var actions = new RecordedSessionEditorActions();
        using var state = new Subject<RecordedSessionEditorState>();
        var workspace = new RecordedSessionSignalsWorkspaceViewModel(
            state,
            sourceVisibility,
            timeline,
            () => extensionSlots,
            actions);
        var changes = TrackPropertyChanges(workspace);
        state.OnNext(CreateState());
        changes.Clear();

        state.OnNext(CreateState(screenState: SessionScreenPresentationState.Loading("Loading session.")));

        Assert.Empty(changes);
    }

    private static void AssertForwardedPropertiesAreDeclared(
        Type adapterType,
        IReadOnlySet<string> forwardedProperties)
    {
        var declared = adapterType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet();

        Assert.All(forwardedProperties, name => Assert.Contains(name, declared));
    }

    private static List<string> TrackPropertyChanges(INotifyPropertyChanged source)
    {
        var changes = new List<string>();
        source.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                changes.Add(args.PropertyName);
            }
        };
        return changes;
    }

    private static RecordedSessionEditorState CreateState(
        int selectedPageIndex = 0,
        TelemetryTimeRange? analysisRange = null,
        BalanceSpeedMode selectedBalanceSpeedMode = BalanceSpeedMode.Both,
        SurfacePresentationState? mapState = null,
        SurfacePresentationState? travelSignalState = null,
        SessionScreenPresentationState? screenState = null,
        SessionOperationPresentationState? operationState = null)
    {
        var preferences = SessionPreferences.Default;
        return new RecordedSessionEditorState(
            Domain: null,
            Load: new RecordedSessionLoadPresentation.Empty(),
            Session: null,
            TelemetryData: null,
            FullTrackPoints: null,
            TrackPoints: null,
            TrackTimelineContext: null,
            Preferences: preferences,
            Intent: new RecordedSessionEditorIntentState(
                SelectedPageIndex: selectedPageIndex,
                AnalysisRange: analysisRange,
                PendingAnalysisRangeBoundary: null,
                SelectedTravelDistributionMode: TravelDistributionMode.ActiveSuspension,
                SelectedBalanceDisplacementMode: BalanceDisplacementMode.Zenith,
                SelectedBalanceSpeedMode: selectedBalanceSpeedMode,
                SelectedVelocityAverageMode: VelocityAverageMode.SampleAveraged,
                SelectedSessionInsightsTargetProfile: SessionInsightsTargetProfile.Trail,
                DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
                SignalDisplayPreferences: preferences.SignalDisplay,
                SignalLayoutPreferences: preferences.SignalLayout,
                LayoutPreferences: preferences.Layout),
            Presentation: new RecordedSessionEditorPresentationState(
                MapState: mapState ?? SurfacePresentationState.Hidden,
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
                    Travel: travelSignalState ?? SurfacePresentationState.Hidden,
                    Velocity: SurfacePresentationState.Hidden,
                    Imu: SurfacePresentationState.Hidden,
                    PitchRoll: SurfacePresentationState.Hidden,
                    Speed: SurfacePresentationState.Hidden,
                    Elevation: SurfacePresentationState.Hidden,
                    ShowAirtime: true,
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
                SignalPlotContextMenuActionsBySignalRowId: EmptySignalPlotContextMenuActionsBySignalRowId,
                ScreenState: screenState ?? SessionScreenPresentationState.Ready,
                OperationState: operationState ?? SessionOperationPresentationState.Hidden),
            AnalysisSelection: new AnalysisSelectionState(null, null, []));
    }

    private static List<RecordedSessionEditorIntent> Subscribe(RecordedSessionEditorActions actions)
    {
        var intents = new List<RecordedSessionEditorIntent>();
        actions.Intents.Subscribe(intents.Add);
        return intents;
    }

    private sealed class TestOwner : ObservableObject
    {
        public void RaiseNameChanged() => OnPropertyChanged("Name");
    }

    private sealed class TestTabPageViewModel(IUiThreadDispatcher uiThreadDispatcher)
        : TabPageViewModelBase(uiThreadDispatcher);
}

using System.ComponentModel;
using System.Reflection;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Services;

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
    [Fact]
    public void RecordedSessionSignalsWorkspace_ForwardsActionsAndContextChanges()
    {
        var context = new RecordedSessionContext();
        using var actions = new RecordedSessionEditorActions();
        var intents = Subscribe(actions);
        var workspace = new RecordedSessionSignalsWorkspaceViewModel(context, actions);
        var changes = TrackPropertyChanges(workspace);

        workspace.SignalLayoutPreferences = context.SignalLayoutPreferences;
        workspace.SetAnalysisRange(1.25, 3.5);
        workspace.ClearAnalysisRange();
        workspace.SetAnalysisRangeBoundary(2.25);
        context.TravelSignalState = SurfacePresentationState.Ready;

        Assert.Collection(
            intents,
            intent => Assert.Equal(context.SignalLayoutPreferences, Assert.IsType<RecordedSessionEditorIntent.SetSignalLayoutPreferences>(intent).Preferences),
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
        var (context, _, _, workspace) = CreateAnalysisWorkspace();
        var changes = TrackPropertyChanges(workspace);

        context.AnalysisRange = new TelemetryTimeRange(1, 3);
        context.SelectedBalanceSpeedMode = BalanceSpeedMode.LowSpeed;

        Assert.Contains("1.0", workspace.SessionAnalysisRangeText, StringComparison.Ordinal);
        Assert.Contains("3.0", workspace.SessionAnalysisRangeText, StringComparison.Ordinal);
        Assert.Contains(nameof(SessionAnalysisWorkspaceViewModel.SessionAnalysisRangeText), changes);
        Assert.Contains(nameof(SessionAnalysisWorkspaceViewModel.SessionAnalysisModesText), changes);
    }

    [Fact]
    public void RecordedSessionContext_SelectedPageState_ClampsAndTracksCollectionChanges()
    {
        var context = new RecordedSessionContext();
        var signals = new PageViewModelBase("Signals");
        var damping = new PageViewModelBase("Damping");
        var changes = TrackPropertyChanges(context);

        context.Pages.Add(signals);
        context.Pages.Add(damping);

        Assert.Equal(2, context.PageCount);
        Assert.Equal(0, context.SelectedPageIndex);
        Assert.Same(signals, context.SelectedPage);
        Assert.Equal("Signals", context.SelectedPageDisplayName);
        Assert.Contains(nameof(RecordedSessionContext.PageCount), changes);
        Assert.Contains(nameof(RecordedSessionContext.SelectedPage), changes);
        Assert.Contains(nameof(RecordedSessionContext.SelectedPageDisplayName), changes);

        changes.Clear();
        context.SelectedPageIndex = 1;

        Assert.Same(damping, context.SelectedPage);
        Assert.Equal("Damping", context.SelectedPageDisplayName);
        Assert.Contains(nameof(RecordedSessionContext.SelectedPageIndex), changes);
        Assert.Contains(nameof(RecordedSessionContext.SelectedPage), changes);
        Assert.Contains(nameof(RecordedSessionContext.PageCount), changes);
        Assert.Contains(nameof(RecordedSessionContext.SelectedPageDisplayName), changes);

        context.SelectedPageIndex = 99;
        Assert.Equal(1, context.SelectedPageIndex);

        context.SelectedPageIndex = -1;
        Assert.Equal(0, context.SelectedPageIndex);

        context.SelectedPageIndex = 1;
        changes.Clear();

        context.Pages.Remove(damping);

        Assert.Equal(0, context.SelectedPageIndex);
        Assert.Same(signals, context.SelectedPage);
        Assert.Equal("Signals", context.SelectedPageDisplayName);
        Assert.Contains(nameof(RecordedSessionContext.SelectedPageIndex), changes);
        Assert.Contains(nameof(RecordedSessionContext.PageCount), changes);

        changes.Clear();

        context.Pages.Clear();

        Assert.Equal(0, context.SelectedPageIndex);
        Assert.Null(context.SelectedPage);
        Assert.Equal(0, context.PageCount);
        Assert.Equal(string.Empty, context.SelectedPageDisplayName);
        Assert.Contains(nameof(RecordedSessionContext.SelectedPage), changes);
        Assert.Contains(nameof(RecordedSessionContext.PageCount), changes);
        Assert.Contains(nameof(RecordedSessionContext.SelectedPageDisplayName), changes);
    }

    private static (RecordedSessionContext Context, TestSessionOperationGateway Gateway, RecordedSessionEditorActions Actions, SessionAnalysisWorkspaceViewModel Workspace) CreateAnalysisWorkspace()
    {
        var context = new RecordedSessionContext();
        var gateway = new TestSessionOperationGateway();
        var actions = new RecordedSessionEditorActions();
        var state = new Subject<RecordedSessionEditorState>();
        var workspace = new SessionAnalysisWorkspaceViewModel(
            state,
            () => context.ExtensionSlots,
            gateway,
            actions,
            new RelayCommand<TelemetryRangeSelection?>(_ => { }),
            Substitute.For<IRecordedSessionAnalysisResultState>());
        context.PropertyChanged += (_, _) =>
            state.OnNext(RecordedSessionEditorStateSnapshot.From(context, SessionPreferences.Default));
        state.OnNext(RecordedSessionEditorStateSnapshot.From(context, SessionPreferences.Default));
        return (context, gateway, actions, workspace);
    }

    [Fact]
    public void SessionMediaWorkspace_TracksSurfaceStateAndExtensionMediaPanes()
    {
        var context = new RecordedSessionContext();
        var workspace = new SessionMediaWorkspaceViewModel(context);
        var changes = TrackPropertyChanges(workspace);

        Assert.False(workspace.HasMediaContent);

        context.MapState = SurfacePresentationState.Ready;

        Assert.True(workspace.HasMediaContent);
        Assert.Contains(nameof(SessionMediaWorkspaceViewModel.MapState), changes);
        Assert.Contains(nameof(SessionMediaWorkspaceViewModel.HasMediaContent), changes);

        changes.Clear();
        context.MapState = SurfacePresentationState.Hidden;
        context.ExtensionSlots.MediaPanes.Add(new RecordedSessionMediaPaneContribution(
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
        var context = new RecordedSessionContext();
        using var actions = new RecordedSessionEditorActions();
        using var state = new Subject<RecordedSessionEditorState>();
        var intents = Subscribe(actions);
        var workspace = new SessionShellMobileWorkspaceViewModel(
            new TestTabPageViewModel(new InlineUiThreadDispatcher()),
            context.Pages,
            state,
            actions);
        var changes = TrackPropertyChanges(workspace);

        context.ScreenState = SessionScreenPresentationState.Loading("Loading session.");
        context.SessionOperationState = SessionOperationPresentationState.Progress("Saving.", 25);
        context.Pages.Add(new PageViewModelBase("Signals"));
        context.Pages.Add(new PageViewModelBase("Damping"));
        context.SelectedPageIndex = 1;
        state.OnNext(RecordedSessionEditorStateSnapshot.From(context, SessionPreferences.Default));

        Assert.Equal(context.ScreenState, workspace.ScreenState);
        Assert.Equal(context.SessionOperationState, workspace.SessionOperationState);
        Assert.Equal(context.SelectedPageIndex, workspace.SelectedPageIndex);
        Assert.Same(context.SelectedPage, workspace.SelectedPage);
        Assert.Equal(context.PageCount, workspace.PageCount);
        Assert.Equal(context.SelectedPageDisplayName, workspace.SelectedPageDisplayName);
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
        var context = new RecordedSessionContext();
        using var actions = new RecordedSessionEditorActions();
        var workspace = new RecordedSessionSignalsWorkspaceViewModel(context, actions);
        var changes = TrackPropertyChanges(workspace);

        context.ScreenState = SessionScreenPresentationState.Loading("Loading session.");

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

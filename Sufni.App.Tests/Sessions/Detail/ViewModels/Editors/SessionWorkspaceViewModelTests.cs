using System.ComponentModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Statistics.ViewModels.Editors;
using Sufni.App.Shared.Base;
using Sufni.App.Sessions.Graph.ViewModels.Editors;
using Sufni.App.Sessions.Media.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class SessionWorkspaceViewModelTests
{
    [Fact]
    public void RecordedSessionGraphWorkspace_ForwardsActionsAndContextChanges()
    {
        var context = new RecordedSessionContext();
        var gateway = new TestSessionOperationGateway();
        var workspace = new RecordedSessionGraphWorkspaceViewModel(context, gateway);
        var changes = TrackPropertyChanges(workspace);

        workspace.GraphPreferences = context.GraphPreferences;
        workspace.SetAnalysisRange(1.25, 3.5);
        workspace.ClearAnalysisRange();
        workspace.SetAnalysisRangeBoundary(2.25);
        context.TravelGraphState = SurfacePresentationState.Ready;

        Assert.Equal(context.GraphPreferences, Assert.Single(gateway.GraphPreferences));
        Assert.Equal((1.25, 3.5), Assert.Single(gateway.AnalysisRanges));
        Assert.Equal(1, gateway.AnalysisRangeClearCount);
        Assert.Equal(2.25, Assert.Single(gateway.AnalysisRangeBoundaries));
        Assert.Equal(SurfacePresentationState.Ready, workspace.TravelGraphState);
        Assert.Contains(nameof(RecordedSessionGraphWorkspaceViewModel.TravelGraphState), changes);
    }

    [Fact]
    public void SessionStatisticsWorkspace_ModeSetters_WriteTheContext()
    {
        var (context, _, workspace) = CreateStatisticsWorkspace();

        workspace.SelectedTravelHistogramMode = TravelHistogramMode.DynamicSag;
        workspace.SelectedBalanceDisplacementMode = BalanceDisplacementMode.Speed;
        workspace.SelectedBalanceSpeedMode = BalanceSpeedMode.HighSpeed;
        workspace.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;
        workspace.SelectedSessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Enduro;

        Assert.Equal(TravelHistogramMode.DynamicSag, context.SelectedTravelHistogramMode);
        Assert.Equal(BalanceDisplacementMode.Speed, context.SelectedBalanceDisplacementMode);
        Assert.Equal(BalanceSpeedMode.HighSpeed, context.SelectedBalanceSpeedMode);
        Assert.Equal(VelocityAverageMode.StrokePeakAveraged, context.SelectedVelocityAverageMode);
        Assert.Equal(SessionAnalysisTargetProfile.Enduro, context.SelectedSessionAnalysisTargetProfile);
    }

    [Fact]
    public async Task SessionStatisticsWorkspace_DampingCallbacks_RouteThroughTheGateway()
    {
        var (_, gateway, workspace) = CreateStatisticsWorkspace();

        workspace.PreviewDampingSpeedCutoff(SuspensionType.Front, DampingSpeedCircuit.Compression, 123);
        workspace.CancelDampingSpeedCutoffPreview();
        await workspace.CommitDampingSpeedCutoffAsync(SuspensionType.Rear, DampingSpeedCircuit.Rebound, 321);

        Assert.Equal((SuspensionType.Front, DampingSpeedCircuit.Compression, 123), Assert.Single(gateway.CutoffPreviews));
        Assert.Equal(1, gateway.CutoffPreviewCancellations);
        Assert.Equal((SuspensionType.Rear, DampingSpeedCircuit.Rebound, 321), Assert.Single(gateway.CutoffCommits));
    }

    [Fact]
    public void SessionStatisticsWorkspace_AnalysisTexts_TrackContextChanges()
    {
        var (context, _, workspace) = CreateStatisticsWorkspace();
        var changes = TrackPropertyChanges(workspace);

        context.AnalysisRange = new TelemetryTimeRange(1, 3);
        context.SelectedBalanceSpeedMode = BalanceSpeedMode.LowSpeed;

        Assert.Contains("1.0", workspace.SessionAnalysisRangeText, StringComparison.Ordinal);
        Assert.Contains("3.0", workspace.SessionAnalysisRangeText, StringComparison.Ordinal);
        Assert.Contains(nameof(SessionStatisticsWorkspaceViewModel.SessionAnalysisRangeText), changes);
        Assert.Contains(nameof(SessionStatisticsWorkspaceViewModel.SessionAnalysisModesText), changes);
    }

    [Fact]
    public void RecordedSessionContext_SelectedPageState_ClampsAndTracksCollectionChanges()
    {
        var context = new RecordedSessionContext();
        var graph = new PageViewModelBase("Graph");
        var damper = new PageViewModelBase("Damper");
        var changes = TrackPropertyChanges(context);

        context.Pages.Add(graph);
        context.Pages.Add(damper);

        Assert.Equal(2, context.PageCount);
        Assert.Equal(0, context.SelectedPageIndex);
        Assert.Same(graph, context.SelectedPage);
        Assert.Equal("Graph", context.SelectedPageDisplayName);
        Assert.Contains(nameof(RecordedSessionContext.PageCount), changes);
        Assert.Contains(nameof(RecordedSessionContext.SelectedPage), changes);
        Assert.Contains(nameof(RecordedSessionContext.SelectedPageDisplayName), changes);

        changes.Clear();
        context.SelectedPageIndex = 1;

        Assert.Same(damper, context.SelectedPage);
        Assert.Equal("Damper", context.SelectedPageDisplayName);
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

        context.Pages.Remove(damper);

        Assert.Equal(0, context.SelectedPageIndex);
        Assert.Same(graph, context.SelectedPage);
        Assert.Equal("Graph", context.SelectedPageDisplayName);
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

    private static (RecordedSessionContext Context, TestSessionOperationGateway Gateway, SessionStatisticsWorkspaceViewModel Workspace) CreateStatisticsWorkspace()
    {
        var context = new RecordedSessionContext();
        var gateway = new TestSessionOperationGateway();
        var workspace = new SessionStatisticsWorkspaceViewModel(
            context,
            gateway,
            new RelayCommand<TelemetryRangeSelection?>(_ => { }));
        return (context, gateway, workspace);
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
        var workspace = new SessionShellMobileWorkspaceViewModel(
            new TestTabPageViewModel(new InlineUiThreadDispatcher()),
            context);
        var changes = TrackPropertyChanges(workspace);

        context.ScreenState = SessionScreenPresentationState.Loading("Loading session.");
        context.SessionOperationState = SessionOperationPresentationState.Progress("Saving.", 25);
        context.Pages.Add(new PageViewModelBase("Graph"));
        context.Pages.Add(new PageViewModelBase("Damper"));
        context.SelectedPageIndex = 1;

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

        Assert.Equal(0, context.SelectedPageIndex);
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
    public void GraphWorkspace_ForwardedPropertiesAreDeclaredPublicProperties() =>
        AssertForwardedPropertiesAreDeclared(
            typeof(RecordedSessionGraphWorkspaceViewModel),
            RecordedSessionGraphWorkspaceViewModel.ForwardedProperties);

    [Fact]
    public void StatisticsWorkspace_ForwardedPropertiesAreDeclaredPublicProperties() =>
        AssertForwardedPropertiesAreDeclared(
            typeof(SessionStatisticsWorkspaceViewModel),
            SessionStatisticsWorkspaceViewModel.ForwardedProperties);

    [Fact]
    public void GraphWorkspace_DoesNotRebroadcastUndeclaredContextProperties()
    {
        var context = new RecordedSessionContext();
        var workspace = new RecordedSessionGraphWorkspaceViewModel(context, new TestSessionOperationGateway());
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

    private sealed class TestOwner : ObservableObject
    {
        public void RaiseNameChanged() => OnPropertyChanged("Name");
    }

    private sealed class TestTabPageViewModel(IUiThreadDispatcher uiThreadDispatcher)
        : TabPageViewModelBase(uiThreadDispatcher);
}

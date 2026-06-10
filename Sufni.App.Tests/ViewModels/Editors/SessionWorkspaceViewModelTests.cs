using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.ExtensionHost.Presentation;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.ExtensionHost.SessionDetails;
using Sufni.App.ExtensionHost.ViewModels.Editors;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.Tests.ViewModels.Editors;

public class SessionWorkspaceViewModelTests
{
    [Fact]
    public void RecordedSessionGraphWorkspace_ForwardsActionsAndContextChanges()
    {
        var context = new RecordedSessionContext();
        SessionGraphPreferences? receivedGraphPreferences = null;
        (double Start, double End)? analysisRange = null;
        var analysisRangeCleared = false;
        double? analysisBoundary = null;
        var workspace = new RecordedSessionGraphWorkspaceViewModel(
            context,
            preferences => receivedGraphPreferences = preferences,
            (start, end) => analysisRange = (start, end),
            () => analysisRangeCleared = true,
            boundary => analysisBoundary = boundary);
        var changes = TrackPropertyChanges(workspace);

        workspace.GraphPreferences = context.GraphPreferences;
        workspace.SetAnalysisRange(1.25, 3.5);
        workspace.ClearAnalysisRange();
        workspace.SetAnalysisRangeBoundary(2.25);
        context.TravelGraphState = SurfacePresentationState.Ready;

        Assert.Equal(context.GraphPreferences, receivedGraphPreferences);
        Assert.Equal((1.25, 3.5), analysisRange);
        Assert.True(analysisRangeCleared);
        Assert.Equal(2.25, analysisBoundary);
        Assert.Equal(SurfacePresentationState.Ready, workspace.TravelGraphState);
        Assert.Contains(nameof(RecordedSessionGraphWorkspaceViewModel.TravelGraphState), changes);
    }

    [Fact]
    public async Task SessionStatisticsWorkspace_ForwardsModeAndDampingCallbacks()
    {
        var context = new RecordedSessionContext();
        TravelHistogramMode? travelMode = null;
        BalanceDisplacementMode? displacementMode = null;
        BalanceSpeedMode? speedMode = null;
        VelocityAverageMode? velocityMode = null;
        SessionAnalysisTargetProfile? targetProfile = null;
        (SuspensionType Side, DampingSpeedCircuit Circuit, double Cutoff)? preview = null;
        var previewCanceled = false;
        (SuspensionType Side, DampingSpeedCircuit Circuit, double Cutoff)? committed = null;
        var workspace = new SessionStatisticsWorkspaceViewModel(
            context,
            value => travelMode = value,
            value => displacementMode = value,
            value => speedMode = value,
            value => velocityMode = value,
            value => targetProfile = value,
            new RelayCommand<TelemetryRangeSelection?>(_ => { }),
            (side, circuit, cutoff) => preview = (side, circuit, cutoff),
            () => previewCanceled = true,
            (side, circuit, cutoff) =>
            {
                committed = (side, circuit, cutoff);
                return Task.CompletedTask;
            });
        var changes = TrackPropertyChanges(workspace);

        workspace.SelectedTravelHistogramMode = TravelHistogramMode.DynamicSag;
        workspace.SelectedBalanceDisplacementMode = BalanceDisplacementMode.Speed;
        workspace.SelectedBalanceSpeedMode = BalanceSpeedMode.HighSpeed;
        workspace.SelectedVelocityAverageMode = VelocityAverageMode.StrokePeakAveraged;
        workspace.SelectedSessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Enduro;
        workspace.PreviewDampingSpeedCutoff(SuspensionType.Front, DampingSpeedCircuit.Compression, 123);
        workspace.CancelDampingSpeedCutoffPreview();
        await workspace.CommitDampingSpeedCutoffAsync(SuspensionType.Rear, DampingSpeedCircuit.Rebound, 321);
        context.AnalysisRange = new TelemetryTimeRange(1, 3);
        context.SelectedBalanceSpeedMode = BalanceSpeedMode.LowSpeed;

        Assert.Equal(TravelHistogramMode.DynamicSag, travelMode);
        Assert.Equal(BalanceDisplacementMode.Speed, displacementMode);
        Assert.Equal(BalanceSpeedMode.HighSpeed, speedMode);
        Assert.Equal(VelocityAverageMode.StrokePeakAveraged, velocityMode);
        Assert.Equal(SessionAnalysisTargetProfile.Enduro, targetProfile);
        Assert.Equal((SuspensionType.Front, DampingSpeedCircuit.Compression, 123), preview);
        Assert.True(previewCanceled);
        Assert.Equal((SuspensionType.Rear, DampingSpeedCircuit.Rebound, 321), committed);
        Assert.Equal("Selected range 1.0-3.0s", workspace.SessionAnalysisRangeText);
        Assert.Contains(nameof(SessionStatisticsWorkspaceViewModel.SessionAnalysisRangeText), changes);
        Assert.Contains(nameof(SessionStatisticsWorkspaceViewModel.SessionAnalysisModesText), changes);
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
        var workspace = new SessionShellMobileWorkspaceViewModel(context);
        var changes = TrackPropertyChanges(workspace);

        context.ScreenState = SessionScreenPresentationState.Loading("Loading session.");
        context.SessionOperationState = SessionOperationPresentationState.Progress("Saving.", 25);

        Assert.Equal(context.ScreenState, workspace.ScreenState);
        Assert.Equal(context.SessionOperationState, workspace.SessionOperationState);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.ScreenState), changes);
        Assert.Contains(nameof(SessionShellMobileWorkspaceViewModel.SessionOperationState), changes);
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
}

using Sufni.App.Shared.Plots;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Video.Stores;
using Sufni.App.Video.Models;
using System.Reactive.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.Acquisition.Models;
using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Views.Plots;
using Sufni.Telemetry;

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
using Sufni.App.Tests.TestSupport.Video;
using Sufni.App.Video.ViewModels;
namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

public class SessionWorkspaceViewModelTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> EmptySignalPlotContextMenuActionsBySignalRowId =
        new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>();

    [Fact]
    public void SessionMediaWorkspace_TracksMapState()
    {
        using var state = new Subject<RecordedSessionEditorState>();
        using var workspace = new SessionMediaWorkspaceViewModel(
            state,
            null,
            new SessionTimelineLinkViewModel(),
            videoPane: null,
            TestSegmentComparisons.Create());
        var changes = TrackPropertyChanges(workspace);
        state.OnNext(CreateState());

        Assert.False(workspace.HasMediaContent);

        state.OnNext(CreateState(mapState: SurfacePresentationState.Ready));

        Assert.True(workspace.HasMediaContent);
        Assert.Contains(nameof(SessionMediaWorkspaceViewModel.MapState), changes);
        Assert.Contains(nameof(SessionMediaWorkspaceViewModel.HasMediaContent), changes);
    }

    [Fact]
    public async Task SessionMediaWorkspace_ShowsTheComparedMatchOnTheMap()
    {
        using var segmentComparison = await TestSegmentComparisons.CreateWithMatchAsync();
        using var state = new Subject<RecordedSessionEditorState>();
        using var workspace = new SessionMediaWorkspaceViewModel(
            state,
            null,
            new SessionTimelineLinkViewModel(),
            videoPane: null,
            segmentComparison);
        var changes = TrackPropertyChanges(workspace);
        Assert.Null(workspace.MatchedTrack);

        segmentComparison.TravelRows[0].HeaderActions.Single(action => action.Id == "compare-analysis").Command!.Execute(null);

        Assert.Equal(2, workspace.MatchedTrack?.SessionTrack.Count);
        Assert.Contains(nameof(SessionMediaWorkspaceViewModel.MatchedTrack), changes);
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
        var preferences = SessionPreferences.Default with
        {
            Analysis = SessionPreferences.Default.Analysis with { BalanceSpeedMode = selectedBalanceSpeedMode },
        };
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
                DampingSpeedCutoffs: DampingSpeedCutoffs.Default),
            Presentation: new RecordedSessionEditorPresentationState(
                MapState: mapState ?? SurfacePresentationState.Hidden,
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

}

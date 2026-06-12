using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.SessionDetails;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

public sealed partial class RecordedSessionContext : ObservableObject
{
    public ObservableCollection<PageViewModelBase> Pages { get; } = [];

    public SessionTimelineLinkViewModel Timeline { get; } = new();

    public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();

    [ObservableProperty] private SessionSnapshot? sessionSnapshot;
    [ObservableProperty] private TelemetryData? telemetryData;
    [ObservableProperty] private TelemetryTimeRange? analysisRange;
    [ObservableProperty] private List<TrackPoint>? fullTrackPoints;
    [ObservableProperty] private List<TrackPoint>? trackPoints;
    [ObservableProperty] private TrackTimeRange? trackTimelineContext;
    [ObservableProperty] private MapViewModel? mapViewModel;
    [ObservableProperty] private string? mediaUrl;
    [ObservableProperty] private double? mediaColumnWidth;
    [ObservableProperty] private SurfacePresentationState mapState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState mediaPaneState = SurfacePresentationState.Hidden;
    [ObservableProperty] private RecordedSessionExtensionSlots extensionSlots = new();
    [ObservableProperty] private SurfacePresentationState travelGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState velocityGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState imuGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState pitchRollGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState speedGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState elevationGraphState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState frontStatisticsState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState rearStatisticsState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState compressionBalanceState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState reboundBalanceState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState frontForkVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState frontFrameVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState rearForkVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SurfacePresentationState rearFrameVibrationState = SurfacePresentationState.Hidden;
    [ObservableProperty] private SessionPlotPreferences plotPreferences = SessionPreferences.Default.Plots;
    [ObservableProperty] private SessionGraphPreferences graphPreferences = SessionPreferences.Default.Graph;
    [ObservableProperty] private SessionLayoutPreferences layoutPreferences = SessionPreferences.Default.Layout;
    [ObservableProperty] private TravelHistogramMode selectedTravelHistogramMode = TravelHistogramMode.ActiveSuspension;
    [ObservableProperty] private BalanceDisplacementMode selectedBalanceDisplacementMode = BalanceDisplacementMode.Zenith;
    [ObservableProperty] private BalanceSpeedMode selectedBalanceSpeedMode = BalanceSpeedMode.Both;
    [ObservableProperty] private VelocityAverageMode selectedVelocityAverageMode = VelocityAverageMode.SampleAveraged;
    [ObservableProperty] private SessionAnalysisTargetProfile selectedSessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Trail;
    [ObservableProperty] private SessionDamperPercentages damperPercentages = SessionDamperPercentages.Empty;
    [ObservableProperty] private DampingSpeedCutoffs dampingSpeedCutoffs = DampingSpeedCutoffs.Default;
    [ObservableProperty] private DampingSpeedCutoffs plotDampingSpeedCutoffs = DampingSpeedCutoffs.Default;
    [ObservableProperty] private bool canEditDampingSpeedCutoffs;
    [ObservableProperty] private SessionAnalysisResult sessionAnalysis = SessionAnalysisResult.Hidden;
    [ObservableProperty] private TelemetryRangeSelection? selectedFrontRangeSelection;
    [ObservableProperty] private TelemetryRangeSelection? selectedRearRangeSelection;
    [ObservableProperty] private IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> plotContextMenuActionsByRowId =
        new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>();
    [ObservableProperty] private bool showAirtime = true;
    [ObservableProperty] private bool showVelocityAirtime;
    [ObservableProperty] private bool showImuAirtime;
    [ObservableProperty] private bool showPitchRollAirtime;
    [ObservableProperty] private bool showSpeedAirtime;
    [ObservableProperty] private bool showElevationAirtime;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatisticsSelection))]
    private IReadOnlyList<TelemetryHighlightRange> statisticsSelectionHighlightRanges = [];
    [ObservableProperty] private bool showStatisticsSelection;
    [ObservableProperty] private bool showVelocityStatisticsSelection;
    [ObservableProperty] private bool showImuStatisticsSelection;
    [ObservableProperty] private bool showPitchRollStatisticsSelection;
    [ObservableProperty] private bool showSpeedStatisticsSelection;
    [ObservableProperty] private bool showElevationStatisticsSelection;
    [ObservableProperty] private IReadOnlyList<TelemetryPlotRowAction> travelHeaderActions = [];
    [ObservableProperty] private IReadOnlyList<TelemetryPlotRowAction> velocityHeaderActions = [];
    [ObservableProperty] private IReadOnlyList<TelemetryPlotRowAction> imuHeaderActions = [];
    [ObservableProperty] private IReadOnlyList<TelemetryPlotRowAction> pitchRollHeaderActions = [];
    [ObservableProperty] private IReadOnlyList<TelemetryPlotRowAction> speedHeaderActions = [];
    [ObservableProperty] private IReadOnlyList<TelemetryPlotRowAction> elevationHeaderActions = [];
    [ObservableProperty] private SessionScreenPresentationState screenState = SessionScreenPresentationState.Ready;
    [ObservableProperty] private SessionOperationPresentationState sessionOperationState = SessionOperationPresentationState.Hidden;

    public bool HasStatisticsSelection => StatisticsSelectionHighlightRanges.Count > 0;

    partial void OnMediaUrlChanged(string? value)
    {
        MediaPaneState = string.IsNullOrWhiteSpace(value)
            ? SurfacePresentationState.Hidden
            : SurfacePresentationState.Ready;
    }
}

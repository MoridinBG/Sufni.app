using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.Telemetry;

using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Graph.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

public sealed partial class RecordedSessionContext : ObservableObject
{
    public ObservableCollection<PageViewModelBase> Pages { get; } = [];

    private int selectedPageIndex;

    public RecordedSessionContext()
    {
        Pages.CollectionChanged += OnPagesChanged;
    }

    public int SelectedPageIndex
    {
        get => selectedPageIndex;
        set => SetSelectedPageIndex(value);
    }

    public PageViewModelBase? SelectedPage => Pages.Count == 0 ? null : Pages[SelectedPageIndex];

    public int PageCount => Pages.Count;

    public string SelectedPageDisplayName => SelectedPage?.DisplayName ?? string.Empty;

    public SessionTimelineLinkViewModel Timeline { get; } = new();

    public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();

    [ObservableProperty] public partial SessionSnapshot? SessionSnapshot { get; set; }
    [ObservableProperty] public partial TelemetryData? TelemetryData { get; set; }
    [ObservableProperty] public partial TelemetryTimeRange? AnalysisRange { get; set; }
    [ObservableProperty] public partial List<TrackPoint>? FullTrackPoints { get; set; }
    [ObservableProperty] public partial List<TrackPoint>? TrackPoints { get; set; }
    [ObservableProperty] public partial TrackTimeRange? TrackTimelineContext { get; set; }
    [ObservableProperty] public partial MapViewModel? MapViewModel { get; set; }
    [ObservableProperty] public partial string? MediaUrl { get; set; }
    [ObservableProperty] public partial double? MediaColumnWidth { get; set; }
    [ObservableProperty] public partial SurfacePresentationState MapState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState MediaPaneState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial RecordedSessionExtensionSlots ExtensionSlots { get; set; } = new();
    [ObservableProperty] public partial SurfacePresentationState TravelGraphState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState VelocityGraphState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState ImuGraphState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState PitchRollGraphState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState SpeedGraphState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState ElevationGraphState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState FrontStatisticsState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState RearStatisticsState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState CompressionBalanceState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState ReboundBalanceState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState FrontForkVibrationState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState FrontFrameVibrationState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState RearForkVibrationState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState RearFrameVibrationState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SessionPlotPreferences PlotPreferences { get; set; } = SessionPreferences.Default.Plots;
    [ObservableProperty] public partial SessionGraphPreferences GraphPreferences { get; set; } = SessionPreferences.Default.Graph;
    [ObservableProperty] public partial SessionLayoutPreferences LayoutPreferences { get; set; } = SessionPreferences.Default.Layout;
    [ObservableProperty] public partial TravelHistogramMode SelectedTravelHistogramMode { get; set; } = TravelHistogramMode.ActiveSuspension;
    [ObservableProperty] public partial BalanceDisplacementMode SelectedBalanceDisplacementMode { get; set; } = BalanceDisplacementMode.Zenith;
    [ObservableProperty] public partial BalanceSpeedMode SelectedBalanceSpeedMode { get; set; } = BalanceSpeedMode.Both;
    [ObservableProperty] public partial VelocityAverageMode SelectedVelocityAverageMode { get; set; } = VelocityAverageMode.SampleAveraged;
    [ObservableProperty] public partial SessionAnalysisTargetProfile SelectedSessionAnalysisTargetProfile { get; set; } = SessionAnalysisTargetProfile.Trail;
    [ObservableProperty] public partial SessionDamperPercentages DamperPercentages { get; set; } = SessionDamperPercentages.Empty;
    [ObservableProperty] public partial DampingSpeedCutoffs DampingSpeedCutoffs { get; set; } = DampingSpeedCutoffs.Default;
    [ObservableProperty] public partial DampingSpeedCutoffs PlotDampingSpeedCutoffs { get; set; } = DampingSpeedCutoffs.Default;
    [ObservableProperty] public partial bool CanEditDampingSpeedCutoffs { get; set; }
    [ObservableProperty] public partial SessionAnalysisResult SessionAnalysis { get; set; } = SessionAnalysisResult.Hidden;
    [ObservableProperty] public partial TelemetryRangeSelection? SelectedFrontRangeSelection { get; set; }
    [ObservableProperty] public partial TelemetryRangeSelection? SelectedRearRangeSelection { get; set; }
    [ObservableProperty] public partial IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> PlotContextMenuActionsByRowId { get; set; } =
        new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>();
    [ObservableProperty] public partial bool ShowAirtime { get; set; } = true;
    [ObservableProperty] public partial bool ShowVelocityAirtime { get; set; }
    [ObservableProperty] public partial bool ShowImuAirtime { get; set; }
    [ObservableProperty] public partial bool ShowPitchRollAirtime { get; set; }
    [ObservableProperty] public partial bool ShowSpeedAirtime { get; set; }
    [ObservableProperty] public partial bool ShowElevationAirtime { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatisticsSelection))]
    public partial IReadOnlyList<TelemetryHighlightRange> StatisticsSelectionHighlightRanges { get; set; } = [];
    [ObservableProperty] public partial bool ShowStatisticsSelection { get; set; }
    [ObservableProperty] public partial bool ShowVelocityStatisticsSelection { get; set; }
    [ObservableProperty] public partial bool ShowImuStatisticsSelection { get; set; }
    [ObservableProperty] public partial bool ShowPitchRollStatisticsSelection { get; set; }
    [ObservableProperty] public partial bool ShowSpeedStatisticsSelection { get; set; }
    [ObservableProperty] public partial bool ShowElevationStatisticsSelection { get; set; }
    [ObservableProperty] public partial IReadOnlyList<TelemetryPlotRowAction> TravelHeaderActions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<TelemetryPlotRowAction> VelocityHeaderActions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<TelemetryPlotRowAction> ImuHeaderActions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<TelemetryPlotRowAction> PitchRollHeaderActions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<TelemetryPlotRowAction> SpeedHeaderActions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<TelemetryPlotRowAction> ElevationHeaderActions { get; set; } = [];
    [ObservableProperty] public partial SessionScreenPresentationState ScreenState { get; set; } = SessionScreenPresentationState.Ready;
    [ObservableProperty] public partial SessionOperationPresentationState SessionOperationState { get; set; } = SessionOperationPresentationState.Hidden;

    public bool HasStatisticsSelection => StatisticsSelectionHighlightRanges.Count > 0;

    partial void OnMediaUrlChanged(string? value)
    {
        MediaPaneState = string.IsNullOrWhiteSpace(value)
            ? SurfacePresentationState.Hidden
            : SurfacePresentationState.Ready;
    }

    private void OnPagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        var clampedIndex = ClampSelectedPageIndex(selectedPageIndex);
        SetProperty(ref selectedPageIndex, clampedIndex, nameof(SelectedPageIndex));
        NotifySelectedPagePropertiesChanged();
    }

    private void SetSelectedPageIndex(int value)
    {
        var clampedIndex = ClampSelectedPageIndex(value);
        if (SetProperty(ref selectedPageIndex, clampedIndex, nameof(SelectedPageIndex)))
        {
            NotifySelectedPagePropertiesChanged();
        }
    }

    private int ClampSelectedPageIndex(int value)
    {
        if (Pages.Count == 0)
        {
            return 0;
        }

        if (value < 0)
        {
            return 0;
        }

        if (value >= Pages.Count)
        {
            return Pages.Count - 1;
        }

        return value;
    }

    private void NotifySelectedPagePropertiesChanged()
    {
        OnPropertyChanged(nameof(SelectedPage));
        OnPropertyChanged(nameof(PageCount));
        OnPropertyChanged(nameof(SelectedPageDisplayName));
    }
}

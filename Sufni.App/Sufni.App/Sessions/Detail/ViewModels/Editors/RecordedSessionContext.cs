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
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

public sealed partial class RecordedSessionContext : ObservableObject
{
    public ObservableCollection<PageViewModelBase> Pages { get; }

    private int selectedPageIndex;

    public RecordedSessionContext(
        ObservableCollection<PageViewModelBase>? pages = null,
        SessionTimelineLinkViewModel? timeline = null,
        TelemetrySourceVisibilityStore? sourceVisibility = null)
    {
        Pages = pages ?? [];
        Timeline = timeline ?? new SessionTimelineLinkViewModel();
        SourceVisibility = sourceVisibility ?? new TelemetrySourceVisibilityStore();
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

    public SessionTimelineLinkViewModel Timeline { get; }

    public TelemetrySourceVisibilityStore SourceVisibility { get; }

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
    [ObservableProperty] public partial SurfacePresentationState TravelSignalState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState VelocitySignalState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState ImuSignalState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState PitchRollSignalState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState SpeedSignalState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState ElevationSignalState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState FrontAnalysisState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState RearAnalysisState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState CompressionBalanceState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState ReboundBalanceState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState FrontForkVibrationState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState FrontFrameVibrationState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState RearForkVibrationState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SurfacePresentationState RearFrameVibrationState { get; set; } = SurfacePresentationState.Hidden;
    [ObservableProperty] public partial SignalDisplayPreferences SignalDisplayPreferences { get; set; } = SessionPreferences.Default.SignalDisplay;
    [ObservableProperty] public partial SignalLayoutPreferences SignalLayoutPreferences { get; set; } = SessionPreferences.Default.SignalLayout;
    [ObservableProperty] public partial SessionLayoutPreferences LayoutPreferences { get; set; } = SessionPreferences.Default.Layout;
    [ObservableProperty] public partial TravelDistributionMode SelectedTravelDistributionMode { get; set; } = TravelDistributionMode.ActiveSuspension;
    [ObservableProperty] public partial BalanceDisplacementMode SelectedBalanceDisplacementMode { get; set; } = BalanceDisplacementMode.Zenith;
    [ObservableProperty] public partial BalanceSpeedMode SelectedBalanceSpeedMode { get; set; } = BalanceSpeedMode.Both;
    [ObservableProperty] public partial VelocityAverageMode SelectedVelocityAverageMode { get; set; } = VelocityAverageMode.SampleAveraged;
    [ObservableProperty] public partial SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile { get; set; } = SessionInsightsTargetProfile.Trail;
    [ObservableProperty] public partial SessionDampingPercentages DampingPercentages { get; set; } = SessionDampingPercentages.Empty;
    [ObservableProperty] public partial DampingSpeedCutoffs DampingSpeedCutoffs { get; set; } = DampingSpeedCutoffs.Default;
    [ObservableProperty] public partial DampingSpeedCutoffs PlotDampingSpeedCutoffs { get; set; } = DampingSpeedCutoffs.Default;
    [ObservableProperty] public partial bool CanEditDampingSpeedCutoffs { get; set; }
    [ObservableProperty] public partial SessionInsightsResult SessionInsights { get; set; } = SessionInsightsResult.Hidden;
    [ObservableProperty] public partial TelemetryRangeSelection? ActiveFrontAnalysisSelection { get; set; }
    [ObservableProperty] public partial TelemetryRangeSelection? ActiveRearAnalysisSelection { get; set; }
    [ObservableProperty] public partial IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId { get; set; } =
        new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>();
    [ObservableProperty] public partial bool ShowAirtime { get; set; } = true;
    [ObservableProperty] public partial bool ShowVelocityAirtime { get; set; }
    [ObservableProperty] public partial bool ShowImuAirtime { get; set; }
    [ObservableProperty] public partial bool ShowPitchRollAirtime { get; set; }
    [ObservableProperty] public partial bool ShowSpeedAirtime { get; set; }
    [ObservableProperty] public partial bool ShowElevationAirtime { get; set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAnalysisSelection))]
    public partial IReadOnlyList<TelemetryHighlightRange> AnalysisSelectionHighlightRanges { get; set; } = [];
    [ObservableProperty] public partial bool ShowAnalysisSelection { get; set; }
    [ObservableProperty] public partial bool ShowVelocityAnalysisSelection { get; set; }
    [ObservableProperty] public partial bool ShowImuAnalysisSelection { get; set; }
    [ObservableProperty] public partial bool ShowPitchRollAnalysisSelection { get; set; }
    [ObservableProperty] public partial bool ShowSpeedAnalysisSelection { get; set; }
    [ObservableProperty] public partial bool ShowElevationAnalysisSelection { get; set; }
    [ObservableProperty] public partial IReadOnlyList<SignalRowAction> TravelHeaderActions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SignalRowAction> VelocityHeaderActions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SignalRowAction> ImuHeaderActions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SignalRowAction> PitchRollHeaderActions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SignalRowAction> SpeedHeaderActions { get; set; } = [];
    [ObservableProperty] public partial IReadOnlyList<SignalRowAction> ElevationHeaderActions { get; set; } = [];
    [ObservableProperty] public partial SessionScreenPresentationState ScreenState { get; set; } = SessionScreenPresentationState.Ready;
    [ObservableProperty] public partial SessionOperationPresentationState SessionOperationState { get; set; } = SessionOperationPresentationState.Hidden;

    public bool HasAnalysisSelection => AnalysisSelectionHighlightRanges.Count > 0;

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

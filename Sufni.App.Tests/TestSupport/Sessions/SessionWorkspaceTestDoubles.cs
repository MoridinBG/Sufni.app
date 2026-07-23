using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.Acquisition.Models;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Sessions;

internal sealed class TestSessionAnalysisWorkspace : ISessionAnalysisWorkspace, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public TestSessionAnalysisWorkspace(
        TelemetryData? telemetryData = null,
        bool hasFrontAnalysis = true,
        bool hasRearAnalysis = true,
        bool hasCompressionBalance = true,
        bool hasReboundBalance = true,
        bool hasFrontForkVibration = false,
        bool hasFrontFrameVibration = false,
        bool hasRearForkVibration = false,
        bool hasRearFrameVibration = false,
        TelemetryTimeRange? analysisRange = null,
        RecordedSessionExtensionSlots? extensionSlots = null)
    {
        TelemetryData = telemetryData;
        AnalysisRange = analysisRange;
        ExtensionSlots = extensionSlots ?? new RecordedSessionExtensionSlots();
        FrontAnalysisState = State(hasFrontAnalysis);
        RearAnalysisState = State(hasRearAnalysis);
        CompressionBalanceState = State(hasCompressionBalance);
        ReboundBalanceState = State(hasReboundBalance);
        FrontForkVibrationState = State(hasFrontForkVibration);
        FrontFrameVibrationState = State(hasFrontFrameVibration);
        RearForkVibrationState = State(hasRearForkVibration);
        RearFrameVibrationState = State(hasRearFrameVibration);
    }

    public TelemetryData? TelemetryData { get; set; }
    public TelemetryTimeRange? AnalysisRange { get; set; }
    public TravelDistributionMode SelectedTravelDistributionMode { get; set; } = TravelDistributionMode.ActiveSuspension;
    public BalanceDisplacementMode SelectedBalanceDisplacementMode { get; set; } = BalanceDisplacementMode.Zenith;
    public BalanceSpeedMode SelectedBalanceSpeedMode { get; set; } = BalanceSpeedMode.Both;
    public VelocityAverageMode SelectedVelocityAverageMode { get; set; } = VelocityAverageMode.SampleAveraged;
    public SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile { get; set; } = SessionInsightsTargetProfile.Trail;
    public RecordedSessionExtensionSlots ExtensionSlots { get; }
    public IReadOnlyList<TravelDistributionModeOption> TravelDistributionModeOptions { get; } =
    [
        new(TravelDistributionMode.ActiveSuspension, "Active suspension", "Uses compression and rebound stroke samples."),
        new(TravelDistributionMode.DynamicSag, "Dynamic sag", "Uses every selected travel sample."),
    ];
    public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } =
    [
        new(BalanceDisplacementMode.Zenith, "Zenith", "Plots each stroke at deepest travel."),
        new(BalanceDisplacementMode.Travel, "Travel", "Plots each stroke by travel distance."),
        new(BalanceDisplacementMode.Speed, "Speed", "Plots each stroke at peak speed travel."),
    ];
    public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } =
    [
        new(BalanceSpeedMode.Both, "Both", "Uses all matching strokes."),
        new(BalanceSpeedMode.LowSpeed, "Low speed", "Uses low-speed strokes."),
        new(BalanceSpeedMode.HighSpeed, "High speed", "Uses high-speed strokes."),
    ];
    public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } =
    [
        new(VelocityAverageMode.SampleAveraged, "Sample-averaged", "Counts samples inside strokes."),
        new(VelocityAverageMode.StrokePeakAveraged, "Stroke-peak average", "Counts each stroke peak once."),
    ];
    public IReadOnlyList<SessionInsightsTargetProfileOption> SessionInsightsTargetProfileOptions { get; } =
    [
        new(SessionInsightsTargetProfile.Weekend, "Weekend", "Recreational pace."),
        new(SessionInsightsTargetProfile.Trail, "Trail", "Trail-riding pace."),
        new(SessionInsightsTargetProfile.Enduro, "Enduro", "Rough descending pace."),
        new(SessionInsightsTargetProfile.DH, "DH", "Downhill race pace."),
    ];
    public string SessionAnalysisRangeText { get; set; } = "Full session";
    public string SessionAnalysisModesText { get; set; } = "Travel: Active suspension  Velocity: Sample-averaged  Balance: Zenith / Both";
    public SurfacePresentationState FrontAnalysisState { get; set; }
    public SurfacePresentationState RearAnalysisState { get; set; }
    public SurfacePresentationState CompressionBalanceState { get; set; }
    public SurfacePresentationState ReboundBalanceState { get; set; }
    public SurfacePresentationState FrontForkVibrationState { get; set; }
    public SurfacePresentationState FrontFrameVibrationState { get; set; }
    public SurfacePresentationState RearForkVibrationState { get; set; }
    public SurfacePresentationState RearFrameVibrationState { get; set; }
    public SessionDampingPercentages DampingPercentages { get; set; } = new(10, 20, 30, 40, 50, 60, 70, 80);
    public DampingSpeedCutoffs DampingSpeedCutoffs { get; set; } = DampingSpeedCutoffs.Default;
    public DampingSpeedCutoffs PlotDampingSpeedCutoffs { get; set; } = DampingSpeedCutoffs.Default;
    public bool CanEditDampingSpeedCutoffs { get; set; } = true;
    public SessionInsightsResult SessionInsights { get; set; } = SessionInsightsResult.Hidden;
    public RequestCounter SessionInsightsRequests { get; } = new();
    public IRelayCommand<TelemetryRangeSelection?> SelectAnalysisRangeCommand { get; } =
        new RelayCommand<TelemetryRangeSelection?>(_ => { });
    public TelemetryRangeSelection? ActiveFrontAnalysisSelection { get; set; }
    public TelemetryRangeSelection? ActiveRearAnalysisSelection { get; set; }

    public void RequestSessionInsights() => SessionInsightsRequests.Increment();
    public void PreviewDampingSpeedCutoff(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond) { }
    public void CancelDampingSpeedCutoffPreview() { }
    public Task CommitDampingSpeedCutoffAsync(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond) =>
        Task.CompletedTask;

    public void NotifyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static SurfacePresentationState State(bool ready) =>
        ready ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
}

internal sealed class TestRecordedSessionSignalsWorkspace : IRecordedSessionSignalsWorkspace, INotifyPropertyChanged
{
    private TelemetryTimeRange? analysisRange;

    public event PropertyChangedEventHandler? PropertyChanged;

    public TestRecordedSessionSignalsWorkspace(
        TelemetryData? telemetryData = null,
        SurfacePresentationState? travelSignalState = null,
        SurfacePresentationState? velocitySignalState = null,
        SurfacePresentationState? imuSignalState = null,
        SurfacePresentationState? pitchRollSignalState = null,
        SurfacePresentationState? speedSignalState = null,
        SurfacePresentationState? elevationSignalState = null,
        RecordedSessionExtensionSlots? extensionSlots = null,
        IRecordedSessionAnalysisResultState? analysisResultState = null)
    {
        TelemetryData = telemetryData;
        AnalysisResultState = analysisResultState ?? Substitute.For<IRecordedSessionAnalysisResultState>();
        TravelSignalState = travelSignalState ?? StateForTravel(telemetryData);
        VelocitySignalState = velocitySignalState ?? TravelSignalState;
        ImuSignalState = imuSignalState ?? StateForImu(telemetryData);
        PitchRollSignalState = pitchRollSignalState ?? SurfacePresentationState.Hidden;
        SpeedSignalState = speedSignalState ?? SurfacePresentationState.Hidden;
        ElevationSignalState = elevationSignalState ?? SurfacePresentationState.Hidden;
        ExtensionSlots = extensionSlots ?? new RecordedSessionExtensionSlots();
    }

    public TelemetryData? TelemetryData { get; }
    public IRecordedSessionAnalysisResultState AnalysisResultState { get; }
    public TelemetryTimeRange? AnalysisRange
    {
        get => analysisRange;
        private set
        {
            if (analysisRange == value)
            {
                return;
            }

            analysisRange = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AnalysisRange)));
        }
    }
    public IReadOnlyList<TrackPoint>? TrackPoints { get; set; } =
    [
        new TrackPoint(0, 0, 0, 100, 5),
        new TrackPoint(1, 1, 1, 101, 6),
    ];
    public TrackTimeRange? TrackTimelineContext { get; set; } = new(0, 1);
    public SurfacePresentationState TravelSignalState { get; set; }
    public SurfacePresentationState VelocitySignalState { get; set; }
    public SurfacePresentationState ImuSignalState { get; set; }
    public SurfacePresentationState PitchRollSignalState { get; set; }
    public SurfacePresentationState SpeedSignalState { get; set; }
    public SurfacePresentationState ElevationSignalState { get; set; }
    public SignalDisplayPreferences SignalDisplayPreferences { get; set; } = new();
    public SignalLayoutPreferences SignalLayoutPreferences { get; set; } = SignalLayoutPreferences.Default;
    public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();
    public SessionTimelineLinkViewModel Timeline { get; } = new();
    public RecordedSessionExtensionSlots ExtensionSlots { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> SignalPlotContextMenuActionsBySignalRowId { get; set; } =
        new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>();
    public bool ShowAirtime { get; set; } = true;
    public bool ShowVelocityAirtime { get; set; }
    public bool ShowImuAirtime { get; set; }
    public bool ShowPitchRollAirtime { get; set; }
    public bool ShowSpeedAirtime { get; set; }
    public bool ShowElevationAirtime { get; set; }
    public IReadOnlyList<TelemetryHighlightRange> AnalysisSelectionHighlightRanges { get; set; } = [];
    public bool HasAnalysisSelection { get; set; }
    public bool ShowAnalysisSelection { get; set; }
    public bool ShowVelocityAnalysisSelection { get; set; }
    public bool ShowImuAnalysisSelection { get; set; }
    public bool ShowPitchRollAnalysisSelection { get; set; }
    public bool ShowSpeedAnalysisSelection { get; set; }
    public bool ShowElevationAnalysisSelection { get; set; }
    public IReadOnlyList<SignalRowAction> TravelHeaderActions { get; set; } = [];
    public IReadOnlyList<SignalRowAction> VelocityHeaderActions { get; set; } = [];
    public IReadOnlyList<SignalRowAction> ImuHeaderActions { get; set; } = [];
    public IReadOnlyList<SignalRowAction> PitchRollHeaderActions { get; set; } = [];
    public IReadOnlyList<SignalRowAction> SpeedHeaderActions { get; set; } = [];
    public IReadOnlyList<SignalRowAction> ElevationHeaderActions { get; set; } = [];
    public RequestCounter AnalysisRangeSetRequests { get; } = new();
    public RequestCounter AnalysisRangeClearRequests { get; } = new();
    public RequestCounter AnalysisRangeBoundaryRequests { get; } = new();
    public double? LastAnalysisRangeBoundary { get; private set; }

    public void SetAnalysisRange(double startSeconds, double endSeconds)
    {
        AnalysisRangeSetRequests.Increment();
        AnalysisRange = TelemetryTimeRange.TryCreate(startSeconds, endSeconds, out var range)
            ? range
            : null;
    }

    public void ClearAnalysisRange()
    {
        AnalysisRangeClearRequests.Increment();
        AnalysisRange = null;
    }

    public void SetAnalysisRangeBoundary(double boundarySeconds)
    {
        AnalysisRangeBoundaryRequests.Increment();
        LastAnalysisRangeBoundary = boundarySeconds;
    }

    public void NotifyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static SurfacePresentationState StateForTravel(TelemetryData? telemetryData) =>
        telemetryData is null ? SurfacePresentationState.Hidden : SurfacePresentationState.Ready;

    private static SurfacePresentationState StateForImu(TelemetryData? telemetryData) =>
        telemetryData?.ImuData is null ? SurfacePresentationState.Hidden : SurfacePresentationState.Ready;
}

internal sealed class TestSessionMediaWorkspace : ISessionMediaWorkspace, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public TestSessionMediaWorkspace(
        IReadOnlyList<TrackPoint>? trackPoints = null,
        string? mediaUrl = null,
        MapViewModel? mapViewModel = null,
        RecordedSessionExtensionSlots? extensionSlots = null)
    {
        MapViewModel = mapViewModel ?? CreateMapViewModel(trackPoints ?? []);
        MediaUrl = mediaUrl;
        ExtensionSlots = extensionSlots ?? new RecordedSessionExtensionSlots();
    }

    public bool HasMediaContent =>
        MapState.ReservesLayout ||
        MediaPaneState.ReservesLayout ||
        ExtensionSlots.MediaPanes.Count > 0;
    public MapViewModel? MapViewModel { get; set; }
    public SurfacePresentationState MapState => MapViewModel?.SessionTrackPoints?.Count > 0
        ? SurfacePresentationState.Ready
        : SurfacePresentationState.Hidden;
    public SurfacePresentationState MediaPaneState => !string.IsNullOrWhiteSpace(MediaUrl)
        ? SurfacePresentationState.Ready
        : SurfacePresentationState.Hidden;
    public SessionTimelineLinkViewModel Timeline { get; } = new();
    public RecordedSessionExtensionSlots ExtensionSlots { get; }
    public double? MediaColumnWidth { get; set; } = 400;
    public string? MediaUrl { get; set; }

    public void NotifyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static MapViewModel CreateMapViewModel(IReadOnlyList<TrackPoint> trackPoints)
    {
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
        tileLayerService.AvailableLayers.Returns([]);
        var dialogService = Substitute.For<IDialogService>();
        return new MapViewModel(tileLayerService, dialogService, new InlineUiThreadDispatcher())
        {
            SessionTrackPoints = trackPoints.ToList(),
            FullTrackPoints = [],
        };
    }
}

internal sealed class RequestCounter
{
    public int Count { get; private set; }

    public void Increment() => Count++;
}

internal static class TestRecordedSessionExtensionContributions
{
    public static RecordedSessionAnalysisTabContribution AddAnalysisTab(
        this RecordedSessionExtensionSlots slots,
        string contributionId = "analysis-tab",
        string displayName = "Extension",
        int requestedIndex = 0,
        IRecordedSessionAnalysisTabContributionViewModel? viewModel = null)
    {
        var contribution = new RecordedSessionAnalysisTabContribution(
            "test",
            contributionId,
            Order: 0,
            displayName,
            requestedIndex,
            viewModel ?? new TestContributionViewModel());
        slots.AnalysisTabs.Add(contribution);
        return contribution;
    }

    public static RecordedSessionMediaPaneContribution AddMediaPane(
        this RecordedSessionExtensionSlots slots,
        string contributionId = "media-pane",
        IRecordedSessionMediaPaneContributionViewModel? viewModel = null)
    {
        var contribution = new RecordedSessionMediaPaneContribution(
            "test",
            contributionId,
            Order: 0,
            viewModel ?? new TestContributionViewModel());
        slots.MediaPanes.Add(contribution);
        return contribution;
    }

    public static RecordedSessionToolbarViewContribution AddSignalToolbarView(
        this RecordedSessionExtensionSlots slots,
        string contributionId = "toolbar-view",
        IRecordedSessionToolbarContributionViewModel? viewModel = null)
    {
        var contribution = new RecordedSessionToolbarViewContribution(
            "test",
            contributionId,
            Order: 0,
            RecordedSessionToolbarZone.Trailing,
            viewModel ?? new TestContributionViewModel());
        slots.SignalToolbarViews.Add(contribution);
        return contribution;
    }
}

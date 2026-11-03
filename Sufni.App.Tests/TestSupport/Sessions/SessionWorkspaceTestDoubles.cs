using Sufni.App.Sessions.Signals.Views.Controls;
using Sufni.App.Shared.Plots;
using Sufni.App.MapsAndTracks.Coordinators;
using Microsoft.Extensions.Time.Testing;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.Acquisition.Models;
using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Common;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;
using Sufni.Telemetry;
using Sufni.App.Video.ViewModels;
namespace Sufni.App.Tests.TestSupport.Sessions;

internal sealed class TestSessionAnalysisWorkspace : ISessionAnalysisWorkspace, INotifyPropertyChanged, IDisposable
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
        TelemetryTimeRange? analysisRange = null)
    {
        TelemetryData = telemetryData;
        AnalysisRange = analysisRange;
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
    public SegmentComparisonViewModel SegmentComparison { get; } = TestSegmentComparisons.Create();
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
    public IRelayCommand<TelemetryRangeSelection?> SelectAnalysisRangeCommand { get; } =
        new RelayCommand<TelemetryRangeSelection?>(_ => { });
    public TelemetryRangeSelection? ActiveFrontAnalysisSelection { get; set; }
    public TelemetryRangeSelection? ActiveRearAnalysisSelection { get; set; }
    public PositionVelocityPageViewModel? PositionVelocity { get; set; }

    public void RequestSessionInsights() { }
    public void PreviewDampingSpeedCutoff(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond) { }
    public void CancelDampingSpeedCutoffPreview() { }
    public Task CommitDampingSpeedCutoffAsync(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond) =>
        Task.CompletedTask;

    public void NotifyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose() => SegmentComparison.Dispose();

    private static SurfacePresentationState State(bool ready) =>
        ready ? SurfacePresentationState.Ready : SurfacePresentationState.Hidden;
}

internal sealed class TestSessionMediaWorkspace : ISessionMediaWorkspace, INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly MapViewModel? ownedMap;

    public TestSessionMediaWorkspace(
        IReadOnlyList<TrackPoint>? trackPoints = null,
        VideoPaneViewModel? video = null,
        MapViewModel? mapViewModel = null)
    {
        MapViewModel = mapViewModel ?? (ownedMap = CreateMapViewModel(trackPoints ?? []));
        Video = video;
    }

    public bool HasMediaContent => MapState.ReservesLayout || Video is not null;
    public MapViewModel? MapViewModel { get; set; }
    public SurfacePresentationState MapState => MapViewModel?.SessionTrackPoints?.Count > 0
        ? SurfacePresentationState.Ready
        : SurfacePresentationState.Hidden;
    public SessionTimelineLinkViewModel Timeline { get; } = new();
    public VideoPaneViewModel? Video { get; set; }
    public MatchedTrackOverlay? MatchedTrack { get; set; }

    public void NotifyChanged(string propertyName) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose() => ownedMap?.Dispose();

    private static MapViewModel CreateMapViewModel(IReadOnlyList<TrackPoint> trackPoints)
    {
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultLayerState();
        var dialogService = Substitute.For<IDialogService>();
        return new MapViewModel(tileLayerService, Substitute.For<IMapLayerCoordinator>(), dialogService, new MapGeometryFactory(new InlineBackgroundTaskRunner()), new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider())
        {
            SessionTrackPoints = trackPoints.ToList(),
            FullTrackPoints = [],
        };
    }
}

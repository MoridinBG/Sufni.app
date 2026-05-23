using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.SessionGraphs;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views;
using Sufni.App.Views.Controls;
using Sufni.App.Views.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Views.SessionPages;

public class RecordedGraphPageViewTests
{
    [AvaloniaFact]
    public async Task RecordedGraphPageView_AppliesStoredGraphPreferences()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;
        var workspace = new RecordedGraphPageWorkspaceStub(
            telemetry,
            SurfacePresentationState.Ready,
            SurfacePresentationState.Ready,
            speedGraphState: SurfacePresentationState.Ready)
        {
            GraphPreferences = new SessionGraphPreferences(
            [
                new SessionGraphRowPreferences(TelemetryGraphRowIds.Speed, isExpanded: false),
                new SessionGraphRowPreferences(
                    TelemetryGraphRowIds.Imu,
                    children:
                    [
                        new SessionGraphRowPreferences(TelemetryGraphRowIds.Velocity),
                    ]),
            ]),
        };
        var page = new RecordedGraphPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var root = GetGraphRoot(mounted.View);
        Assert.Equal(
            ["GPS speed (km/h)", "Vibration RMS (g)", "Travel (mm)"],
            root.Rows.Select(row => row.Title!).ToArray());
        Assert.False(root.Rows[0].IsExpanded);
        Assert.Equal(["Elevation (m)"], root.Rows[0].ChildRows.Select(row => row.Title!).ToArray());
        Assert.Equal(["Velocity (m/s)", "Frame pitch/roll (deg)"], root.Rows[1].ChildRows.Select(row => row.Title!).ToArray());
    }

    [AvaloniaFact]
    public async Task RecordedGraphPageView_ShowsNoGraphDataFallback_WhenBothStatesHidden()
    {
        var workspace = new RecordedGraphPageWorkspaceStub(
            null,
            SurfacePresentationState.Hidden,
            SurfacePresentationState.Hidden);

        var page = new RecordedGraphPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var fallback = mounted.View.FindControl<SurfacePlaceholderCard>("NoGraphDataPlaceholder");
        var root = GetGraphRoot(mounted.View);

        Assert.NotNull(fallback);
        Assert.True(fallback!.IsVisible);
        Assert.All(GetRows(root), row => Assert.False(row.IsVisible));
    }

    [AvaloniaFact]
    public async Task RecordedGraphPageView_HidesNoGraphDataPlaceholder_WhenOnlyVelocityGraphIsHidden()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        telemetry.ImuData = TestTelemetryData.CreateWithImu().ImuData;
        var workspace = new RecordedGraphPageWorkspaceStub(
            telemetry,
            SurfacePresentationState.Ready,
            SurfacePresentationState.Ready,
            velocityGraphState: SurfacePresentationState.Hidden);
        var page = new RecordedGraphPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var fallback = mounted.View.FindControl<SurfacePlaceholderCard>("NoGraphDataPlaceholder");

        Assert.NotNull(fallback);
        Assert.False(fallback!.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedGraphPageView_RendersMapBelowGraphs_WhenMapReady()
    {
        var graphWorkspace = new RecordedGraphPageWorkspaceStub(
            TestTelemetryData.CreateProcessed(),
            SurfacePresentationState.Ready,
            SurfacePresentationState.Hidden);
        var mediaWorkspace = CreateMediaWorkspace(
        [
            new TrackPoint(0, 0, 0, null),
            new TrackPoint(1, 100, 100, null),
        ]);

        var page = new RecordedGraphPageViewModel(graphWorkspace, mediaWorkspace);

        await using var mounted = await MountAsync(page);

        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().Single();

        Assert.NotNull(mapHost);
        Assert.True(mapHost!.IsVisible);
        Assert.True(mapView.IsVisible);
        Assert.Same(mediaWorkspace.MapViewModel, mapView.DataContext);
        Assert.Same(mediaWorkspace.Timeline, mapView.Timeline);
        Assert.NotNull(mapView.FindControl<ComboBox>("TileProviderComboBox"));
    }

    private static async Task<MountedRecordedGraphPageView> MountAsync(RecordedGraphPageViewModel page)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new RecordedGraphPageView
        {
            DataContext = page,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedRecordedGraphPageView(host, view);
    }

    private static TelemetryPlotsRoot GetGraphRoot(RecordedGraphPageView view)
    {
        var root = view.GetVisualDescendants()
            .OfType<TelemetryPlotsRoot>()
            .SingleOrDefault(root => root.Name == "GraphRoot");
        Assert.NotNull(root);
        return root!;
    }

    private static TelemetryPlotRow GetBaseRow(TelemetryPlotsRoot root, string title)
        => Assert.Single(root.Rows, row => row.Title == title);

    private static TelemetryPlotRow GetChildRow(TelemetryPlotRow row, string title)
        => Assert.Single(row.ChildRows, child => child.Title == title);

    private static IReadOnlyList<TelemetryPlotRow> GetRows(TelemetryPlotsRoot root)
        => root.Rows.Concat(root.Rows.SelectMany(row => row.ChildRows)).ToArray();

    private static SessionMediaWorkspaceStub CreateMediaWorkspace(IReadOnlyList<TrackPoint> trackPoints)
    {
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
        tileLayerService.AvailableLayers.Returns(new ObservableCollection<TileLayerConfig>());

        var mapViewModel = new MapViewModel(tileLayerService, Substitute.For<IDialogService>(), new InlineUiThreadDispatcher())
        {
            FullTrackPoints = [],
            SessionTrackPoints = trackPoints.ToList(),
        };

        return new SessionMediaWorkspaceStub(mapViewModel);
    }

    private sealed class RecordedGraphPageWorkspaceStub(
        TelemetryData? telemetryData,
        SurfacePresentationState travelGraphState,
        SurfacePresentationState imuGraphState,
        SurfacePresentationState? pitchRollGraphState = null,
        SurfacePresentationState? velocityGraphState = null,
        SurfacePresentationState? speedGraphState = null,
        SurfacePresentationState? elevationGraphState = null,
        TelemetryTimeRange? analysisRange = null) : IRecordedSessionGraphWorkspace
    {
        public TelemetryData? TelemetryData { get; } = telemetryData;
        public TelemetryTimeRange? AnalysisRange { get; private set; } = analysisRange;
        public IReadOnlyList<TrackPoint>? TrackPoints { get; } =
        [
            new TrackPoint(0, 0, 0, 100, 5),
            new TrackPoint(1, 100, 100, 101, 6),
        ];
        public TrackTimeRange? TrackTimelineContext { get; } = new(0, 1);
        public SurfacePresentationState TravelGraphState { get; } = travelGraphState;
        public SurfacePresentationState VelocityGraphState { get; } = velocityGraphState ?? travelGraphState;
        public SurfacePresentationState ImuGraphState { get; } = imuGraphState;
        public SurfacePresentationState PitchRollGraphState { get; } = pitchRollGraphState ?? SurfacePresentationState.Hidden;
        public SurfacePresentationState SpeedGraphState { get; } = speedGraphState ?? SurfacePresentationState.Hidden;
        public SurfacePresentationState ElevationGraphState { get; } = elevationGraphState ?? SurfacePresentationState.Hidden;
        public SessionPlotPreferences PlotPreferences { get; } = new();
        public SessionGraphPreferences GraphPreferences { get; set; } = SessionGraphPreferences.Default;
        public TelemetrySourceVisibilityStore SourceVisibility { get; } = new();
        public SessionTimelineLinkViewModel Timeline { get; } = new();

        public void SetAnalysisRange(double startSeconds, double endSeconds)
        {
            AnalysisRange = TelemetryTimeRange.TryCreate(startSeconds, endSeconds, out var range)
                ? range
                : null;
        }

        public void ClearAnalysisRange()
        {
            AnalysisRange = null;
        }

        public void SetAnalysisRangeBoundary(double boundarySeconds) { }
    }

    private sealed class SessionMediaWorkspaceStub : ISessionMediaWorkspace
    {
        private readonly MapViewModel mapViewModel;

        public SessionMediaWorkspaceStub(MapViewModel mapViewModel)
        {
            this.mapViewModel = mapViewModel;
        }

        public bool HasMediaContent => MapState.ReservesLayout;
        public MapViewModel? MapViewModel => mapViewModel;
        public SurfacePresentationState MapState => mapViewModel.SessionTrackPoints?.Count > 0
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState VideoState => SurfacePresentationState.Hidden;
        public SessionTimelineLinkViewModel Timeline { get; } = new();
        public double? MapVideoWidth => 400;
        public string? VideoUrl => null;
    }
}

internal sealed record MountedRecordedGraphPageView(Window Host, RecordedGraphPageView View) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}

using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.DesktopViews.Plots;
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
    public async Task RecordedGraphPageView_RendersPlots_WhenStatesReady()
    {
        var telemetry = TestTelemetryData.Create();
        telemetry.ImuData = TestTelemetryFactories.CreateTelemetryDataWithImu().ImuData;
        var workspace = new RecordedGraphPageWorkspaceStub(
            telemetry,
            SurfacePresentationState.Ready,
            SurfacePresentationState.Ready,
            speedGraphState: SurfacePresentationState.Ready,
            elevationGraphState: SurfacePresentationState.Ready,
            analysisRange: new TelemetryTimeRange(0.25, 0.75));

        var page = new RecordedGraphPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var travelView = mounted.View.FindControl<TravelPlotDesktopView>("Travel");
        var velocityView = mounted.View.FindControl<VelocityPlotDesktopView>("Velocity");
        var imuView = mounted.View.FindControl<ImuPlotDesktopView>("Imu");
        var speedView = mounted.View.FindControl<TrackSignalPlotDesktopView>("Speed");
        var elevationView = mounted.View.FindControl<TrackSignalPlotDesktopView>("Elevation");
        var root = GetGraphRoot(mounted.View);
        var pageScrollViewer = mounted.View.FindControl<ScrollViewer>("PageScrollViewer");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.NotNull(speedView);
        Assert.NotNull(elevationView);
        Assert.NotNull(pageScrollViewer);
        Assert.Equal(
            ["Travel (mm)", "IMU acceleration (g)", "GPS speed (km/h)"],
            root.Rows.Select(row => row.Title!).ToArray());
        Assert.Equal(["Velocity (m/s)"], GetBaseRow(root, "Travel (mm)").ChildRows.Select(row => row.Title!).ToArray());
        Assert.Equal(["Elevation (m)"], GetBaseRow(root, "GPS speed (km/h)").ChildRows.Select(row => row.Title!).ToArray());
        Assert.True(travelView!.IsVisible);
        Assert.True(velocityView!.IsVisible);
        Assert.True(imuView!.IsVisible);
        Assert.True(speedView!.IsVisible);
        Assert.True(elevationView!.IsVisible);
        Assert.Same(workspace, travelView.GraphWorkspace);
        Assert.Same(workspace, velocityView.GraphWorkspace);
        Assert.Same(workspace, imuView.GraphWorkspace);
        Assert.Same(workspace, speedView.GraphWorkspace);
        Assert.Same(workspace, elevationView.GraphWorkspace);
        Assert.Equal(workspace.AnalysisRange, travelView.AnalysisRange);
        Assert.Equal(workspace.AnalysisRange, velocityView.AnalysisRange);
        Assert.Equal(workspace.AnalysisRange, imuView.AnalysisRange);
        Assert.Equal(workspace.AnalysisRange, speedView.AnalysisRange);
        Assert.Equal(workspace.AnalysisRange, elevationView.AnalysisRange);
        Assert.Equal(SessionGraphSettings.RecordedMobileMaximumDisplayHz, travelView.MaximumDisplayHz);
        Assert.Equal(SessionGraphSettings.RecordedMobileMaximumDisplayHz, velocityView.MaximumDisplayHz);
        Assert.Equal(SessionGraphSettings.RecordedMobileMaximumDisplayHz, imuView.MaximumDisplayHz);
        Assert.True(travelView.HideRightAxis);
        Assert.True(velocityView.HideRightAxis);
        Assert.True(imuView.HideRightAxis);
        Assert.True(speedView.HideRightAxis);
        Assert.True(elevationView.HideRightAxis);
        Assert.False(mounted.View.FindControl<SurfacePlaceholderCard>("NoGraphDataPlaceholder")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedGraphPageView_AppliesStoredGraphPreferences()
    {
        var telemetry = TestTelemetryData.Create();
        telemetry.ImuData = TestTelemetryFactories.CreateTelemetryDataWithImu().ImuData;
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
            ["GPS speed (km/h)", "IMU acceleration (g)", "Travel (mm)"],
            root.Rows.Select(row => row.Title!).ToArray());
        Assert.False(root.Rows[0].IsExpanded);
        Assert.Equal(["Elevation (m)"], root.Rows[0].ChildRows.Select(row => row.Title!).ToArray());
        Assert.Equal(["Velocity (m/s)"], root.Rows[1].ChildRows.Select(row => row.Title!).ToArray());
    }

    [AvaloniaFact]
    public async Task RecordedGraphPageView_ShowsPlaceholders_WhenStatesWaiting()
    {
        var workspace = new RecordedGraphPageWorkspaceStub(
            TestTelemetryData.Create(),
            SurfacePresentationState.WaitingForData("Waiting for travel data."),
            SurfacePresentationState.WaitingForData("Waiting for IMU data."),
            speedGraphState: SurfacePresentationState.WaitingForData("Waiting for speed data."),
            elevationGraphState: SurfacePresentationState.WaitingForData("Waiting for elevation data."));

        var page = new RecordedGraphPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var hosts = mounted.View.GetVisualDescendants()
            .OfType<PlaceholderOverlayContainer>()
            .Where(host => host.Name != "MapHost")
            .ToArray();
        Assert.Equal(5, hosts.Length);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[0].PresentationState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[1].PresentationState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[2].PresentationState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[3].PresentationState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[4].PresentationState.Kind);
        Assert.False(mounted.View.FindControl<SurfacePlaceholderCard>("NoGraphDataPlaceholder")!.IsVisible);
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
    public async Task RecordedGraphPageView_CollapsesVelocityRow_WhenVelocityStateHidden()
    {
        var telemetry = TestTelemetryData.Create();
        telemetry.ImuData = TestTelemetryFactories.CreateTelemetryDataWithImu().ImuData;
        var workspace = new RecordedGraphPageWorkspaceStub(
            telemetry,
            SurfacePresentationState.Ready,
            SurfacePresentationState.Ready,
            velocityGraphState: SurfacePresentationState.Hidden);
        var page = new RecordedGraphPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var fallback = mounted.View.FindControl<SurfacePlaceholderCard>("NoGraphDataPlaceholder");
        var root = GetGraphRoot(mounted.View);
        var travelRow = GetBaseRow(root, "Travel (mm)");
        var velocityRow = GetChildRow(travelRow, "Velocity (m/s)");
        var imuRow = GetBaseRow(root, "IMU acceleration (g)");

        Assert.NotNull(fallback);
        Assert.False(fallback!.IsVisible);
        Assert.True(travelRow.IsVisible);
        Assert.False(velocityRow.IsVisible);
        Assert.True(imuRow.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedGraphPageView_RendersMapBelowGraphs_WhenMapReady()
    {
        var graphWorkspace = new RecordedGraphPageWorkspaceStub(
            TestTelemetryData.Create(),
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
        var root = view.FindControl<TelemetryPlotsRoot>("GraphRoot");
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

        var mapViewModel = new MapViewModel(tileLayerService, Substitute.For<IDialogService>())
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
        public SurfacePresentationState SpeedGraphState { get; } = speedGraphState ?? SurfacePresentationState.Hidden;
        public SurfacePresentationState ElevationGraphState { get; } = elevationGraphState ?? SurfacePresentationState.Hidden;
        public SessionPlotPreferences PlotPreferences { get; } = new();
        public SessionGraphPreferences GraphPreferences { get; set; } = SessionGraphPreferences.Default;
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

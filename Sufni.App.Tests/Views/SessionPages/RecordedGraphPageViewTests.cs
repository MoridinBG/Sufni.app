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
            SurfacePresentationState.Ready);

        var page = new RecordedGraphPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var travelView = mounted.View.FindControl<TravelPlotDesktopView>("Travel");
        var velocityView = mounted.View.FindControl<VelocityPlotDesktopView>("Velocity");
        var imuView = mounted.View.FindControl<ImuPlotDesktopView>("Imu");

        Assert.NotNull(travelView);
        Assert.NotNull(velocityView);
        Assert.NotNull(imuView);
        Assert.True(travelView!.IsVisible);
        Assert.True(velocityView!.IsVisible);
        Assert.True(imuView!.IsVisible);
        Assert.Equal(SessionGraphSettings.RecordedMobileMaximumDisplayHz, travelView.MaximumDisplayHz);
        Assert.Equal(SessionGraphSettings.RecordedMobileMaximumDisplayHz, velocityView.MaximumDisplayHz);
        Assert.Equal(SessionGraphSettings.RecordedMobileMaximumDisplayHz, imuView.MaximumDisplayHz);
        Assert.False(mounted.View.FindControl<SurfacePlaceholderCard>("NoGraphDataPlaceholder")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task RecordedGraphPageView_ShowsPlaceholders_WhenStatesWaiting()
    {
        var workspace = new RecordedGraphPageWorkspaceStub(
            TestTelemetryData.Create(),
            SurfacePresentationState.WaitingForData("Waiting for travel data."),
            SurfacePresentationState.WaitingForData("Waiting for IMU data."));

        var page = new RecordedGraphPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var hosts = mounted.View.GetVisualDescendants()
            .OfType<PlaceholderOverlayContainer>()
            .Where(host => host.Name != "MapHost")
            .ToArray();
        Assert.Equal(3, hosts.Length);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[0].PresentationState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[1].PresentationState.Kind);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[2].PresentationState.Kind);
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
        var graphGrid = mounted.View.FindControl<Grid>("GraphGrid");

        Assert.NotNull(fallback);
        Assert.NotNull(graphGrid);
        Assert.True(fallback!.IsVisible);
        Assert.Equal(0, graphGrid!.RowDefinitions[0].Height.Value);
        Assert.Equal(0, graphGrid.RowDefinitions[1].Height.Value);
        Assert.Equal(0, graphGrid.RowDefinitions[2].Height.Value);
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
        var graphGrid = mounted.View.FindControl<Grid>("GraphGrid");

        Assert.NotNull(fallback);
        Assert.NotNull(graphGrid);
        Assert.False(fallback!.IsVisible);
        Assert.NotEqual(0, graphGrid!.RowDefinitions[0].Height.Value);
        Assert.Equal(0, graphGrid.RowDefinitions[1].Height.Value);
        Assert.NotEqual(0, graphGrid.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[0].Height.GridUnitType);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[2].Height.GridUnitType);
        Assert.Equal(graphGrid.RowDefinitions[0].Height.Value, graphGrid.RowDefinitions[2].Height.Value);
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

    private static SessionMediaWorkspaceStub CreateMediaWorkspace(IReadOnlyList<TrackPoint> trackPoints)
    {
        var tileLayerService = Substitute.For<ITileLayerService>();
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
        SurfacePresentationState? velocityGraphState = null) : IRecordedSessionGraphWorkspace
    {
        public TelemetryData? TelemetryData { get; } = telemetryData;
        public TelemetryTimeRange? AnalysisRange { get; private set; }
        public SurfacePresentationState TravelGraphState { get; } = travelGraphState;
        public SurfacePresentationState VelocityGraphState { get; } = velocityGraphState ?? travelGraphState;
        public SurfacePresentationState ImuGraphState { get; } = imuGraphState;
        public SessionPlotPreferences PlotPreferences { get; } = new();
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

        public void SetAnalysisRangeBoundaryFromMarker(double markerSeconds) { }
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
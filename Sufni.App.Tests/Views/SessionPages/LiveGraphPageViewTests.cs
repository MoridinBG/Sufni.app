using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views;
using Sufni.App.Views.Controls;
using Sufni.App.Views.SessionPages;

namespace Sufni.App.Tests.Views.SessionPages;

public class LiveGraphPageViewTests
{
    [AvaloniaFact]
    public async Task LiveGraphPageView_BindsPlaceholderContainers_ToWorkspaceState()
    {
        var workspace = Substitute.For<ILiveSessionGraphWorkspace>();
        workspace.GraphBatches.Returns(new Subject<LiveGraphBatch>());
        workspace.PlotRanges.Returns(new LiveSessionPlotRanges(TravelMaximum: 180, VelocityMaximum: 5, ImuMaximum: 5));
        workspace.Timeline.Returns(new SessionTimelineLinkViewModel());
        workspace.TravelGraphState.Returns(SurfacePresentationState.Ready);
        workspace.VelocityGraphState.Returns(SurfacePresentationState.WaitingForData("Waiting for live velocity data."));
        workspace.ImuGraphState.Returns(SurfacePresentationState.Hidden);
        workspace.SpeedGraphState.Returns(SurfacePresentationState.WaitingForData("Waiting for live speed data."));
        workspace.ElevationGraphState.Returns(SurfacePresentationState.Hidden);
        workspace.TrackPoints.Returns(
        [
            new TrackPoint(0, 0, 0, 100, 5),
            new TrackPoint(1, 100, 100, 101, 6),
        ]);
        workspace.TrackTimelineContext.Returns(new TrackTimeRange(0, 1));

        var page = new LiveGraphPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        var hosts = mounted.View.GetVisualDescendants()
            .OfType<PlaceholderOverlayContainer>()
            .Where(host => host.Name != "MapHost")
            .ToArray();
        var graphGrid = mounted.View.GetVisualDescendants()
            .OfType<Grid>()
            .Single(grid => grid.RowDefinitions.Count == 5 && grid.Children.OfType<PlaceholderOverlayContainer>().Count() == 5);
        Assert.Equal(5, hosts.Length);
        Assert.Equal(SurfacePresentationState.Ready, hosts[0].PresentationState);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[1].PresentationState.Kind);
        Assert.Equal(SurfacePresentationState.Hidden, hosts[2].PresentationState);
        Assert.Equal(SurfaceStateKind.WaitingForData, hosts[3].PresentationState.Kind);
        Assert.Equal(SurfacePresentationState.Hidden, hosts[4].PresentationState);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[0].Height.GridUnitType);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[1].Height.GridUnitType);
        Assert.Equal(GridUnitType.Star, graphGrid.RowDefinitions[3].Height.GridUnitType);
        Assert.Equal(graphGrid.RowDefinitions[0].Height.Value, graphGrid.RowDefinitions[1].Height.Value);
        Assert.Equal(0, graphGrid.RowDefinitions[2].Height.Value);
        Assert.Equal(0, graphGrid.RowDefinitions[4].Height.Value);
    }

    [AvaloniaFact]
    public async Task LiveGraphPageView_ExposesWorkspace_AsDataContextPath()
    {
        var workspace = Substitute.For<ILiveSessionGraphWorkspace>();
        workspace.GraphBatches.Returns(new Subject<LiveGraphBatch>());
        workspace.PlotRanges.Returns(new LiveSessionPlotRanges(TravelMaximum: 180, VelocityMaximum: 5, ImuMaximum: 5));
        workspace.Timeline.Returns(new SessionTimelineLinkViewModel());
        workspace.TravelGraphState.Returns(SurfacePresentationState.Hidden);
        workspace.VelocityGraphState.Returns(SurfacePresentationState.Hidden);
        workspace.ImuGraphState.Returns(SurfacePresentationState.Hidden);
        workspace.SpeedGraphState.Returns(SurfacePresentationState.Hidden);
        workspace.ElevationGraphState.Returns(SurfacePresentationState.Hidden);

        var page = new LiveGraphPageViewModel(workspace, CreateMediaWorkspace([]));

        await using var mounted = await MountAsync(page);

        Assert.Same(workspace, page.Workspace);
        Assert.Same(page, mounted.View.DataContext);
    }

    [AvaloniaFact]
    public async Task LiveGraphPageView_RendersMapBelowGraphs_WhenMapReady()
    {
        var graphWorkspace = Substitute.For<ILiveSessionGraphWorkspace>();
        graphWorkspace.GraphBatches.Returns(new Subject<LiveGraphBatch>());
        graphWorkspace.PlotRanges.Returns(new LiveSessionPlotRanges(TravelMaximum: 180, VelocityMaximum: 5, ImuMaximum: 5));
        graphWorkspace.Timeline.Returns(new SessionTimelineLinkViewModel());
        graphWorkspace.TravelGraphState.Returns(SurfacePresentationState.Ready);
        graphWorkspace.VelocityGraphState.Returns(SurfacePresentationState.Ready);
        graphWorkspace.ImuGraphState.Returns(SurfacePresentationState.Hidden);
        graphWorkspace.SpeedGraphState.Returns(SurfacePresentationState.Hidden);
        graphWorkspace.ElevationGraphState.Returns(SurfacePresentationState.Hidden);
        var mediaWorkspace = CreateMediaWorkspace(
        [
            new TrackPoint(0, 0, 0, null),
            new TrackPoint(1, 100, 100, null),
        ]);

        var page = new LiveGraphPageViewModel(graphWorkspace, mediaWorkspace);

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

    private static async Task<MountedLiveGraphPageView> MountAsync(LiveGraphPageViewModel page)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsurePlotViewStyle();

        var view = new LiveGraphPageView
        {
            DataContext = page,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedLiveGraphPageView(host, view);
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

internal sealed record MountedLiveGraphPageView(Window Host, LiveGraphPageView View) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}

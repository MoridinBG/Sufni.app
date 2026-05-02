using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.DesktopViews.Items;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Items;

public class SessionMediaDesktopViewTests
{
    [AvaloniaFact]
    public async Task SessionMediaDesktopView_HidesMap_WhenSessionTrackIsEmpty()
    {
        var workspace = CreateWorkspace([]);

        await using var mounted = await MountAsync(workspace);

        var mediaGrid = mounted.View.FindControl<Grid>("MediaGrid");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().SingleOrDefault();

        Assert.NotNull(mediaGrid);
        Assert.False(mediaGrid!.IsVisible);
        Assert.Null(mapView);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_ShowsMap_WhenSessionTrackHasPoints()
    {
        var workspace = CreateWorkspace(
        [
            new TrackPoint(1, 2, 3, 4),
        ]);

        await using var mounted = await MountAsync(workspace);

        var mediaGrid = mounted.View.FindControl<Grid>("MediaGrid");
        var mediaSplitter = mounted.View.FindControl<GridSplitter>("MediaSplitter");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().Single();

        Assert.NotNull(mediaGrid);
        Assert.NotNull(mediaSplitter);
        Assert.True(mediaGrid!.IsVisible);
        Assert.True(mapView.IsVisible);
        Assert.Same(workspace.Timeline, mapView.Timeline);
        Assert.False(mediaSplitter!.IsVisible);
        Assert.Equal(0, mediaGrid.RowDefinitions[0].Height.Value);
        Assert.Equal(GridUnitType.Pixel, mediaGrid.RowDefinitions[0].Height.GridUnitType);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_StretchesMap_ToAvailableWidth()
    {
        var workspace = CreateWorkspace(
        [
            new TrackPoint(1, 2, 3, 4),
        ]);

        await using var mounted = await MountAsync(workspace);

        var mediaGrid = mounted.View.FindControl<Grid>("MediaGrid");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().Single();

        Assert.NotNull(mediaGrid);
        Assert.True(mediaGrid!.Bounds.Width > workspace.MapVideoWidth);
        Assert.Equal(mediaGrid.Bounds.Width, mapView.Bounds.Width);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_CollapsesMapRow_WhenOnlyVideoIsPresent()
    {
        var workspace = CreateWorkspace([], videoUrl: "video.mp4");

        await using var mounted = await MountAsync(workspace);

        var mediaGrid = mounted.View.FindControl<Grid>("MediaGrid");
        var videoHost = mounted.View.FindControl<PlaceholderOverlayContainer>("VideoHost");
        var mediaSplitter = mounted.View.FindControl<GridSplitter>("MediaSplitter");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().SingleOrDefault();

        Assert.NotNull(mediaGrid);
        Assert.NotNull(videoHost);
        Assert.NotNull(mediaSplitter);
        Assert.True(mediaGrid!.IsVisible);
        Assert.True(videoHost!.IsVisible);
        Assert.False(mediaSplitter!.IsVisible);
        Assert.Null(mapView);
        Assert.Equal(0, mediaGrid.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Pixel, mediaGrid.RowDefinitions[2].Height.GridUnitType);
    }

    private static async Task<MountedSessionMediaDesktopView> MountAsync(SessionMediaWorkspaceStub workspace)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var view = new SessionMediaDesktopView
        {
            DataContext = workspace,
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedSessionMediaDesktopView(host, view);
    }

    private static SessionMediaWorkspaceStub CreateWorkspace(IReadOnlyList<TrackPoint> trackPoints, string? videoUrl = null)
    {
        var tileLayerService = Substitute.For<ITileLayerService>();
        tileLayerService.AvailableLayers.Returns(new ObservableCollection<TileLayerConfig>());

        var dialogService = Substitute.For<IDialogService>();
        var mapViewModel = new MapViewModel(tileLayerService, dialogService)
        {
            SessionTrackPoints = trackPoints.ToList(),
            FullTrackPoints = [],
        };

        return new SessionMediaWorkspaceStub(mapViewModel, videoUrl);
    }

    private sealed class SessionMediaWorkspaceStub : ISessionMediaWorkspace
    {
        private readonly MapViewModel mapViewModel;
        private readonly string? videoUrl;

        public SessionMediaWorkspaceStub(MapViewModel mapViewModel, string? videoUrl)
        {
            this.mapViewModel = mapViewModel;
            this.videoUrl = videoUrl;
        }

        public bool HasMediaContent => MapState.ReservesLayout || VideoState.ReservesLayout;
        public MapViewModel? MapViewModel => mapViewModel;
        public SurfacePresentationState MapState => mapViewModel.SessionTrackPoints?.Count > 0
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState VideoState => !string.IsNullOrWhiteSpace(VideoUrl)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SessionTimelineLinkViewModel Timeline { get; } = new();
        public double? MapVideoWidth => 400;
        public string? VideoUrl => videoUrl;
    }
}

internal sealed class MountedSessionMediaDesktopView(Window host, SessionMediaDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public SessionMediaDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
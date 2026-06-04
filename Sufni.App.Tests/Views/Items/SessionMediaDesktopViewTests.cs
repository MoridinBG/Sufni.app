using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.DesktopViews.Items;
using Sufni.App.ExtensionHost.RecordedSessions;
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

        var mediaRoot = mounted.View.FindControl<Grid>("MediaContentRoot");
        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().SingleOrDefault();

        Assert.NotNull(mediaRoot);
        Assert.NotNull(mapHost);
        Assert.False(mediaRoot!.IsVisible);
        Assert.False(mapHost!.IsVisible);
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

        var mediaRoot = mounted.View.FindControl<Grid>("MediaContentRoot");
        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost");
        var mediaSplitter = mounted.View.FindControl<GridSplitter>("MediaSplitter");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().Single();

        Assert.NotNull(mediaRoot);
        Assert.NotNull(mapHost);
        Assert.NotNull(mediaSplitter);
        Assert.True(mediaRoot!.IsVisible);
        Assert.True(mapHost!.IsVisible);
        Assert.True(mapView.IsVisible);
        Assert.Same(workspace.ExtensionSlots, mapView.ExtensionSlots);
        Assert.Same(workspace.Timeline, mapView.Timeline);
        Assert.False(mediaSplitter!.IsVisible);
        Assert.True(
            mapHost!.Bounds.Height > mounted.View.Bounds.Height * 0.9,
            $"Expected map-only media to fill the available height. View={mounted.View.Bounds}, MapHost={mapHost.Bounds}.");
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_RendersMediaPaneContributions_WhenOnlyExtensionMediaIsPresent()
    {
        var workspace = CreateWorkspace([]);
        workspace.ExtensionSlots.MediaPanes.Add(new RecordedSessionMediaPaneContribution(
            "extension",
            "media-pane",
            Order: 0,
            new TextBlock { Name = "DesktopMediaPane", Text = "Media pane" }));

        await using var mounted = await MountAsync(workspace);

        var mediaRoot = mounted.View.FindControl<Grid>("MediaContentRoot");
        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost");
        var mediaPanes = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionMediaPanesView>());

        Assert.NotNull(mediaRoot);
        Assert.NotNull(mapHost);
        Assert.NotNull(mediaPanes);
        Assert.True(mediaRoot!.IsVisible);
        Assert.False(mapHost!.IsVisible);
        AssertContributionText(mounted.View, "DesktopMediaPane", "Media pane");
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_CollapsesMapRow_WhenOnlyVideoIsPresent()
    {
        var workspace = CreateWorkspace([], videoUrl: "video.mp4");

        await using var mounted = await MountAsync(workspace);

        var mediaRoot = mounted.View.FindControl<Grid>("MediaContentRoot");
        var videoHost = mounted.View.FindControl<PlaceholderOverlayContainer>("VideoHost");
        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost");
        var mediaSplitter = mounted.View.FindControl<GridSplitter>("MediaSplitter");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().SingleOrDefault();

        Assert.NotNull(mediaRoot);
        Assert.NotNull(videoHost);
        Assert.NotNull(mapHost);
        Assert.NotNull(mediaSplitter);
        Assert.True(mediaRoot!.IsVisible);
        Assert.True(videoHost!.IsVisible);
        Assert.False(mapHost!.IsVisible);
        Assert.False(mediaSplitter!.IsVisible);
        Assert.Null(mapView);
        Assert.Equal(0, mediaRoot.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Pixel, mediaRoot.RowDefinitions[2].Height.GridUnitType);
    }

    private static async Task<MountedSessionMediaDesktopView> MountAsync(SessionMediaWorkspaceStub workspace)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var view = new SessionMediaDesktopView
        {
            DataContext = workspace,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionMediaDesktopView(host, view);
    }

    private static void AssertContributionText(Control root, string name, string text)
    {
        var textBlocks = root.GetVisualDescendants()
            .OfType<TextBlock>()
            .ToArray();
        var textBlock = textBlocks.SingleOrDefault(textBlock => textBlock.Name == name);
        Assert.True(
            textBlock is not null,
            $"Expected contribution text '{name}'. Actual text blocks: {string.Join(", ", textBlocks.Select(block => $"{block.Name}:{block.Text}"))}");
        Assert.Equal(text, textBlock!.Text);
    }

    private static SessionMediaWorkspaceStub CreateWorkspace(IReadOnlyList<TrackPoint> trackPoints, string? videoUrl = null)
    {
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
        tileLayerService.AvailableLayers.Returns(new ObservableCollection<TileLayerConfig>());

        var dialogService = Substitute.For<IDialogService>();
        var mapViewModel = new MapViewModel(tileLayerService, dialogService, new InlineUiThreadDispatcher())
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

        public bool HasMediaContent =>
            MapState.ReservesLayout ||
            VideoState.ReservesLayout ||
            ExtensionSlots.MediaPanes.Count > 0;
        public MapViewModel? MapViewModel => mapViewModel;
        public SurfacePresentationState MapState => mapViewModel.SessionTrackPoints?.Count > 0
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState VideoState => !string.IsNullOrWhiteSpace(VideoUrl)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SessionTimelineLinkViewModel Timeline { get; } = new();
        public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();
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

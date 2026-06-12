using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.DesktopViews.Items;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.Models;
using Sufni.App.Presentation;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views;
using Sufni.App.Views.Controls;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;

namespace Sufni.App.Tests.Views.Items;

[Collection("Ui")]
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
        var contribution = new TestContributionViewModel
        {
            Name = "DesktopMediaPane",
            Content = new TextBlock { Text = "Media pane" },
        };
        workspace.ExtensionSlots.MediaPanes.Add(new RecordedSessionMediaPaneContribution(
            "extension",
            "media-pane",
            Order: 0,
            contribution));

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
        Assert.True(
            contribution.Bounds.Height > mounted.View.Bounds.Height * 0.9,
            $"Expected extension-only media pane to fill the available height. View={mounted.View.Bounds}, Contribution={contribution.Bounds}.");
        AssertContributionText(mounted.View, "Media pane");
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_SplitsMapAndMediaPanes_WhenBothArePresent()
    {
        var workspace = CreateWorkspace(
        [
            new TrackPoint(1, 2, 3, 4),
        ]);
        workspace.ExtensionSlots.MediaPanes.Add(new RecordedSessionMediaPaneContribution(
            "extension",
            "media-pane",
            Order: 0,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "DesktopMediaPane", Text = "Media pane" },
            }));

        await using var mounted = await MountAsync(workspace);

        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost");
        var splitter = mounted.View.FindControl<GridSplitter>("MediaPaneSplitter");
        var mediaPanes = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionMediaPanesView>());

        Assert.NotNull(mapHost);
        Assert.NotNull(splitter);
        Assert.True(splitter!.IsVisible);
        Assert.True(mediaPanes.IsVisible);
        Assert.True(
            mapHost!.Bounds.Height > mounted.View.Bounds.Height * 0.4,
            $"Expected map to receive about half of the media height. View={mounted.View.Bounds}, MapHost={mapHost.Bounds}.");
        Assert.True(
            mediaPanes.Bounds.Height > mounted.View.Bounds.Height * 0.4,
            $"Expected media panes to receive about half of the media height. View={mounted.View.Bounds}, MediaPanes={mediaPanes.Bounds}.");
        AssertContributionText(mounted.View, "Media pane");
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_CollapsesMapRow_WhenOnlyMediaIsPresent()
    {
        var workspace = CreateWorkspace([], mediaUrl: "media.mp4");

        await using var mounted = await MountAsync(workspace);

        var mediaRoot = mounted.View.FindControl<Grid>("MediaContentRoot");
        var mediaHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MediaHost");
        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost");
        var mediaSplitter = mounted.View.FindControl<GridSplitter>("MediaSplitter");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().SingleOrDefault();

        Assert.NotNull(mediaRoot);
        Assert.NotNull(mediaHost);
        Assert.NotNull(mapHost);
        Assert.NotNull(mediaSplitter);
        Assert.True(mediaRoot!.IsVisible);
        Assert.True(mediaHost!.IsVisible);
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

    private static void AssertContributionText(Control root, string text)
    {
        var textBlocks = root.GetVisualDescendants()
            .OfType<TextBlock>()
            .ToArray();
        var textBlock = textBlocks.SingleOrDefault(textBlock => textBlock.Text == text);
        Assert.True(
            textBlock is not null,
            $"Expected contribution text '{text}'. Actual text blocks: {string.Join(", ", textBlocks.Select(block => $"{block.Name}:{block.Text}"))}");
        Assert.Equal(text, textBlock!.Text);
    }

    private static SessionMediaWorkspaceStub CreateWorkspace(IReadOnlyList<TrackPoint> trackPoints, string? mediaUrl = null)
    {
        var tileLayerService = Substitute.For<ITileLayerService>().WithDefaultSelectedLayerChanges();
        tileLayerService.AvailableLayers.Returns(new ObservableCollection<TileLayerConfig>());

        var dialogService = Substitute.For<IDialogService>();
        var mapViewModel = new MapViewModel(tileLayerService, dialogService, new InlineUiThreadDispatcher())
        {
            SessionTrackPoints = trackPoints.ToList(),
            FullTrackPoints = [],
        };

        return new SessionMediaWorkspaceStub(mapViewModel, mediaUrl);
    }

    private sealed class SessionMediaWorkspaceStub : ISessionMediaWorkspace
    {
        private readonly MapViewModel mapViewModel;
        private readonly string? mediaUrl;

        public SessionMediaWorkspaceStub(MapViewModel mapViewModel, string? mediaUrl)
        {
            this.mapViewModel = mapViewModel;
            this.mediaUrl = mediaUrl;
        }

        public bool HasMediaContent =>
            MapState.ReservesLayout ||
            MediaPaneState.ReservesLayout ||
            ExtensionSlots.MediaPanes.Count > 0;
        public MapViewModel? MapViewModel => mapViewModel;
        public SurfacePresentationState MapState => mapViewModel.SessionTrackPoints?.Count > 0
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState MediaPaneState => !string.IsNullOrWhiteSpace(MediaUrl)
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SessionTimelineLinkViewModel Timeline { get; } = new();
        public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();
        public double? MediaColumnWidth => 400;
        public string? MediaUrl => mediaUrl;
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

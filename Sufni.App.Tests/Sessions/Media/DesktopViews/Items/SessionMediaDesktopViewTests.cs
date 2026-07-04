using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Sessions.Media.DesktopViews.Items;
using Sufni.App.Shared.DesktopViews.Controls;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.Views;
using Sufni.App.Sessions.Media.Views.Controls;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Extensions;

namespace Sufni.App.Tests.Sessions.Media.DesktopViews.Items;

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
        var primarySplit = FindSplit(mounted.View, "PrimaryMediaSplit");
        var lowerSplit = FindSplit(mounted.View, "LowerMediaSplit");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().Single();

        Assert.NotNull(mediaRoot);
        Assert.NotNull(mapHost);
        Assert.True(mediaRoot!.IsVisible);
        Assert.True(mapHost!.IsVisible);
        Assert.True(mapView.IsVisible);
        Assert.Same(workspace.ExtensionSlots, mapView.ExtensionSlots);
        Assert.Same(workspace.Timeline, mapView.Timeline);
        Assert.False(FindPart<Border>(primarySplit, "PART_SplitHandle").IsVisible);
        Assert.False(FindPart<Border>(lowerSplit, "PART_SplitHandle").IsVisible);
        Assert.True(
            mapHost.Bounds.Height > mounted.View.Bounds.Height * 0.9,
            $"Expected map-only media to fill the available height. View={mounted.View.Bounds}, MapHost={mapHost.Bounds}.");
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_DoesNotApplyMediaColumnWidthAsRootMinimum()
    {
        var workspace = CreateWorkspace(
            [
                new TrackPoint(1, 2, 3, 4),
            ],
            mediaUrl: "media.mp4");

        await using var mounted = await MountAsync(workspace);

        var mediaRoot = mounted.View.FindControl<Grid>("MediaContentRoot");

        Assert.NotNull(mediaRoot);
        Assert.Equal(0, mediaRoot!.MinWidth);
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
        var primarySplit = FindSplit(mounted.View, "PrimaryMediaSplit");
        var lowerSplit = FindSplit(mounted.View, "LowerMediaSplit");
        var mediaPanes = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionMediaPanesView>());

        Assert.NotNull(mediaRoot);
        Assert.NotNull(mapHost);
        Assert.True(mediaRoot!.IsVisible);
        Assert.False(mapHost!.IsVisible);
        Assert.False(FindPart<Border>(primarySplit, "PART_SplitHandle").IsVisible);
        Assert.False(FindPart<Border>(lowerSplit, "PART_SplitHandle").IsVisible);
        Assert.True(mediaPanes.IsVisible);
        Assert.True(
            contribution.Bounds.Height > mounted.View.Bounds.Height * 0.9,
            $"Expected extension-only media pane to fill the available height. View={mounted.View.Bounds}, Contribution={contribution.Bounds}.");
        AssertContributionText(mounted.View, "Media pane");
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_MediaPaneRebuild_DoesNotDisposeBorrowedContributionViewModel()
    {
        var workspace = CreateWorkspace([]);
        var viewModel = new DisposableContributionViewModel();
        workspace.ExtensionSlots.MediaPanes.Add(new RecordedSessionMediaPaneContribution(
            "extension",
            "media-pane",
            Order: 0,
            viewModel));

        await using var mounted = await MountAsync(workspace);
        var mediaPanes = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionMediaPanesView>());
        Assert.NotNull(mediaPanes);
        Assert.False(viewModel.IsDisposed);

        workspace.ExtensionSlots.MediaPanes.Clear();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(0, viewModel.DisposeCount);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_SplitsMapAndMediaPanes_WhenBothArePresent()
    {
        var workspace = CreateWorkspace(
        [
            new TrackPoint(1, 2, 3, 4),
        ]);
        workspace.ExtensionSlots.MediaPanes.Add(CreateMediaPaneContribution("Media pane"));

        await using var mounted = await MountAsync(workspace);

        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost");
        var primarySplit = FindSplit(mounted.View, "PrimaryMediaSplit");
        var lowerSplit = FindSplit(mounted.View, "LowerMediaSplit");
        var mediaPanes = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionMediaPanesView>());

        Assert.NotNull(mapHost);
        Assert.False(FindPart<Border>(primarySplit, "PART_SplitHandle").IsVisible);
        Assert.True(FindPart<Border>(lowerSplit, "PART_SplitHandle").IsVisible);
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
    public async Task SessionMediaDesktopView_SplitsMediaAndMap_WhenBothArePresent()
    {
        var workspace = CreateWorkspace(
            [
                new TrackPoint(1, 2, 3, 4),
            ],
            mediaUrl: "media.mp4");

        await using var mounted = await MountAsync(workspace);

        var primarySplit = FindSplit(mounted.View, "PrimaryMediaSplit");
        var lowerSplit = FindSplit(mounted.View, "LowerMediaSplit");
        var mediaHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MediaHost")!;
        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost")!;

        Assert.True(FindPart<Border>(primarySplit, "PART_SplitHandle").IsVisible);
        Assert.False(FindPart<Border>(lowerSplit, "PART_SplitHandle").IsVisible);
        Assert.True(mediaHost.IsVisible);
        Assert.True(mapHost.IsVisible);
        Assert.True(mediaHost.Bounds.Height > mounted.View.Bounds.Height * 0.4);
        Assert.True(mapHost.Bounds.Height > mounted.View.Bounds.Height * 0.4);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_SplitsMediaAndExtension_WhenMapIsAbsent()
    {
        var workspace = CreateWorkspace([], mediaUrl: "media.mp4");
        workspace.ExtensionSlots.MediaPanes.Add(CreateMediaPaneContribution("Media pane"));

        await using var mounted = await MountAsync(workspace);

        var primarySplit = FindSplit(mounted.View, "PrimaryMediaSplit");
        var lowerSplit = FindSplit(mounted.View, "LowerMediaSplit");
        var mediaHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MediaHost")!;
        var mediaPanes = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionMediaPanesView>());

        Assert.True(FindPart<Border>(primarySplit, "PART_SplitHandle").IsVisible);
        Assert.False(FindPart<Border>(lowerSplit, "PART_SplitHandle").IsVisible);
        Assert.True(mediaHost.IsVisible);
        Assert.True(mediaPanes.IsVisible);
        Assert.True(mediaHost.Bounds.Height > mounted.View.Bounds.Height * 0.4);
        Assert.True(mediaPanes.Bounds.Height > mounted.View.Bounds.Height * 0.4);
        AssertContributionText(mounted.View, "Media pane");
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_AppliesStoredMediaRowRatios_ForTwoVisiblePanes()
    {
        var workspace = CreateWorkspace(
        [
            new TrackPoint(1, 2, 3, 4),
        ]);
        workspace.ExtensionSlots.MediaPanes.Add(CreateMediaPaneContribution("Media pane"));

        await using var mounted = await MountAsync(
            workspace,
            new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Map, 0.25),
                new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, 0.75),
            ]));

        var lowerSplit = FindSplit(mounted.View, "LowerMediaSplit");
        var (map, extensionMedia) = GetPaneLengths(lowerSplit);

        Assert.Equal(0.25, map.Value);
        Assert.Equal(GridUnitType.Star, map.GridUnitType);
        Assert.Equal(0.75, extensionMedia.Value);
        Assert.Equal(GridUnitType.Star, extensionMedia.GridUnitType);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_AppliesStoredMediaRowRatios_ForThreeVisiblePanes()
    {
        var workspace = CreateWorkspace(
            [
                new TrackPoint(1, 2, 3, 4),
            ],
            mediaUrl: "media.mp4");
        workspace.ExtensionSlots.MediaPanes.Add(CreateMediaPaneContribution("Media pane"));

        await using var mounted = await MountAsync(
            workspace,
            new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.2),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Map, 0.3),
                new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, 0.5),
            ]));

        var primarySplit = FindSplit(mounted.View, "PrimaryMediaSplit");
        var lowerSplit = FindSplit(mounted.View, "LowerMediaSplit");
        var primary = GetPaneLengths(primarySplit);
        var lower = GetPaneLengths(lowerSplit);

        Assert.Equal(0.2, primary.First.Value);
        Assert.Equal(0.8, primary.Second.Value);
        Assert.Equal(0.375, lower.First.Value, precision: 6);
        Assert.Equal(0.625, lower.Second.Value, precision: 6);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_MapCollapsedState_HidesMapContentAndExpandsMedia()
    {
        var workspace = CreateWorkspace(
            [
                new TrackPoint(1, 2, 3, 4),
            ],
            mediaUrl: "media.mp4");
        workspace.ExtensionSlots.MediaPanes.Add(CreateMediaPaneContribution("Media pane"));

        await using var mounted = await MountAsync(
            workspace,
            new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.4),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Map, 0.2, IsCollapsed: true),
                new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, 0.4),
            ]));

        var lowerSplit = FindSplit(mounted.View, "LowerMediaSplit");

        Assert.True(lowerSplit.IsFirstPaneCollapsed);
        var mapHost = FindPart<ContentControl>(lowerSplit, "PART_FirstContentHost");
        Assert.False(mapHost.IsVisible);
        Assert.Null(mapHost.Content);
        Assert.True(FindPart<Button>(lowerSplit, "PART_FirstCollapsedHeader").IsVisible);
        Assert.True(FindPart<ContentControl>(lowerSplit, "PART_SecondContentHost").IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_ExtensionMediaCollapsedState_HidesExtensionContentAndExpandsMap()
    {
        var workspace = CreateWorkspace(
        [
            new TrackPoint(1, 2, 3, 4),
        ]);
        workspace.ExtensionSlots.MediaPanes.Add(CreateMediaPaneContribution("Media pane"));

        await using var mounted = await MountAsync(
            workspace,
            new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Map, 0.8),
                new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, 0.2, IsCollapsed: true),
            ]));

        var lowerSplit = FindSplit(mounted.View, "LowerMediaSplit");

        Assert.True(lowerSplit.IsSecondPaneCollapsed);
        Assert.True(FindPart<ContentControl>(lowerSplit, "PART_FirstContentHost").IsVisible);
        var extensionHost = FindPart<ContentControl>(lowerSplit, "PART_SecondContentHost");
        Assert.False(extensionHost.IsVisible);
        Assert.Null(extensionHost.Content);
        Assert.True(FindPart<Button>(lowerSplit, "PART_SecondCollapsedHeader").IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_MediaCollapsedState_HidesMediaContentAndExpandsMapAndExtension()
    {
        var workspace = CreateWorkspace(
            [
                new TrackPoint(1, 2, 3, 4),
            ],
            mediaUrl: "media.mp4");
        workspace.ExtensionSlots.MediaPanes.Add(CreateMediaPaneContribution("Media pane"));

        await using var mounted = await MountAsync(
            workspace,
            new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.2, IsCollapsed: true),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Map, 0.4),
                new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, 0.4),
            ]));

        var primarySplit = FindSplit(mounted.View, "PrimaryMediaSplit");

        Assert.True(primarySplit.IsFirstPaneCollapsed);
        var mediaHost = FindPart<ContentControl>(primarySplit, "PART_FirstContentHost");
        Assert.False(mediaHost.IsVisible);
        Assert.Null(mediaHost.Content);
        Assert.True(FindPart<Button>(primarySplit, "PART_FirstCollapsedHeader").IsVisible);
        Assert.True(FindPart<ContentControl>(primarySplit, "PART_SecondContentHost").IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_UsesDefaults_WhenStoredMediaPaneSetDoesNotMatch()
    {
        var workspace = CreateWorkspace(
        [
            new TrackPoint(1, 2, 3, 4),
        ]);
        workspace.ExtensionSlots.MediaPanes.Add(CreateMediaPaneContribution("Media pane"));

        await using var mounted = await MountAsync(
            workspace,
            new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference("unknown", 0.25),
                new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, 0.75),
            ]));

        var lowerSplit = FindSplit(mounted.View, "LowerMediaSplit");
        var (map, extensionMedia) = GetPaneLengths(lowerSplit);

        Assert.Equal(1, map.Value);
        Assert.Equal(GridUnitType.Star, map.GridUnitType);
        Assert.Equal(1, extensionMedia.Value);
        Assert.Equal(GridUnitType.Star, extensionMedia.GridUnitType);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_PrimarySplitCommit_DoesNotPersistSyntheticPaneId()
    {
        var workspace = CreateWorkspace(
            [
                new TrackPoint(1, 2, 3, 4),
            ],
            mediaUrl: "media.mp4");
        workspace.ExtensionSlots.MediaPanes.Add(CreateMediaPaneContribution("Media pane"));

        await using var mounted = await MountAsync(
            workspace,
            new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.2),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Map, 0.3),
                new SessionPaneSizePreference(SessionLayoutPaneIds.ExtensionMedia, 0.5),
            ]));

        var primarySplit = FindSplit(mounted.View, "PrimaryMediaSplit");
        primarySplit.BeginDragForTests();
        primarySplit.DragToFirstRatioForTests(0.4);
        primarySplit.CompleteDragForTests();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.NotNull(mounted.View.LayoutPreferences);
        Assert.Equal(
            [SessionLayoutPaneIds.Media, SessionLayoutPaneIds.Map, SessionLayoutPaneIds.ExtensionMedia],
            mounted.View.LayoutPreferences!.Panes.Select(pane => pane.PaneId).ToArray());
        Assert.Equal(0.4, mounted.View.LayoutPreferences.Panes[0].Ratio, precision: 6);
        Assert.Equal(0.225, mounted.View.LayoutPreferences.Panes[1].Ratio, precision: 6);
        Assert.Equal(0.375, mounted.View.LayoutPreferences.Panes[2].Ratio, precision: 6);
    }

    [AvaloniaFact]
    public async Task SessionMediaDesktopView_CollapsesMapRow_WhenOnlyMediaIsPresent()
    {
        var workspace = CreateWorkspace([], mediaUrl: "media.mp4");

        await using var mounted = await MountAsync(workspace);

        var mediaRoot = mounted.View.FindControl<Grid>("MediaContentRoot");
        var mediaHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MediaHost");
        var mapHost = mounted.View.FindControl<PlaceholderOverlayContainer>("MapHost");
        var primarySplit = FindSplit(mounted.View, "PrimaryMediaSplit");
        var lowerSplit = mounted.View.GetVisualDescendants()
            .OfType<CollapsibleSplitView>()
            .SingleOrDefault(split => split.Name == "LowerMediaSplit");
        var mapView = mounted.View.GetVisualDescendants().OfType<MapView>().SingleOrDefault();

        Assert.NotNull(mediaRoot);
        Assert.NotNull(mediaHost);
        Assert.NotNull(mapHost);
        Assert.True(mediaRoot!.IsVisible);
        Assert.True(mediaHost!.IsVisible);
        Assert.False(mapHost!.IsVisible);
        Assert.False(FindPart<Border>(primarySplit, "PART_SplitHandle").IsVisible);
        Assert.Null(lowerSplit);
        Assert.False(FindPart<ContentControl>(primarySplit, "PART_SecondContentHost").IsVisible);
        Assert.Null(mapView);
    }

    private static async Task<MountedSessionMediaDesktopView> MountAsync(
        SessionMediaWorkspaceStub workspace,
        SessionPaneGroupPreferences? layoutPreferences = null)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var view = new SessionMediaDesktopView
        {
            DataContext = workspace,
            LayoutPreferences = layoutPreferences,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionMediaDesktopView(host, view);
    }

    private static RecordedSessionMediaPaneContribution CreateMediaPaneContribution(string text)
    {
        return new RecordedSessionMediaPaneContribution(
            "extension",
            "media-pane",
            Order: 0,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "DesktopMediaPane", Text = text },
            });
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

    private static CollapsibleSplitView FindSplit(SessionMediaDesktopView view, string name)
    {
        return view.GetVisualDescendants()
            .OfType<CollapsibleSplitView>()
            .Single(split => split.Name == name);
    }

    private static T FindPart<T>(CollapsibleSplitView split, string name)
        where T : Control
    {
        return split.GetVisualDescendants()
            .OfType<T>()
            .Single(control =>
                control.Name == name &&
                control.FindAncestorOfType<CollapsibleSplitView>() == split);
    }

    private static (GridLength First, GridLength Second) GetPaneLengths(CollapsibleSplitView split)
    {
        var grid = Assert.IsType<Grid>(split.Content);
        if (split.Orientation == Orientation.Horizontal)
        {
            return (grid.ColumnDefinitions[0].Width, grid.ColumnDefinitions[2].Width);
        }

        return (grid.RowDefinitions[0].Height, grid.RowDefinitions[2].Height);
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

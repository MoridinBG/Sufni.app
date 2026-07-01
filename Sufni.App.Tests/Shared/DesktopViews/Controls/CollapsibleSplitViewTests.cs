using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.DesktopViews.Controls;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shared.DesktopViews.Controls;

[Collection("Ui")]
public class CollapsibleSplitViewTests
{
    [AvaloniaFact]
    public async Task CollapsibleSplitView_AppliesStoredRatios()
    {
        var view = CreateView();
        view.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference("first", 0.25),
            new SessionPaneSizePreference("second", 0.75),
        ]);

        await using var mounted = await MountAsync(view);

        var (first, second) = GetPaneLengths(mounted.View);
        Assert.Equal(0.25, first.Value);
        Assert.Equal(0.75, second.Value);
        Assert.Equal(GridUnitType.Star, first.GridUnitType);
        Assert.Equal(GridUnitType.Star, second.GridUnitType);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_AppliesStoredCollapsedFirstPane()
    {
        var view = CreateView();
        view.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference("first", 0.2, IsCollapsed: true),
            new SessionPaneSizePreference("second", 0.8),
        ]);

        await using var mounted = await MountAsync(view);

        Assert.True(mounted.View.IsFirstPaneCollapsed);
        Assert.False(mounted.View.IsSecondPaneCollapsed);
        var firstHost = FindPart<ContentControl>(mounted.View, "PART_FirstContentHost");
        Assert.False(firstHost.IsVisible);
        Assert.Null(firstHost.Content);
        var header = FindPart<Button>(mounted.View, "PART_FirstCollapsedHeader");
        Assert.True(header.IsVisible);
        Assert.IsType<PathIcon>(header.Content);
        var secondHost = FindPart<ContentControl>(mounted.View, "PART_SecondContentHost");
        Assert.True(secondHost.IsVisible);
        Assert.Same(view.SecondContent, secondHost.Content);
        Assert.False(FindPart<Border>(mounted.View, "PART_SplitHandle").IsVisible);
        var (first, second) = GetPaneLengths(mounted.View);
        Assert.Equal(34, first.Value);
        Assert.Equal(GridUnitType.Pixel, first.GridUnitType);
        Assert.Equal(GridUnitType.Star, second.GridUnitType);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_AppliesStoredCollapsedSecondPane()
    {
        var view = CreateView();
        view.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference("first", 0.8),
            new SessionPaneSizePreference("second", 0.2, IsCollapsed: true),
        ]);

        await using var mounted = await MountAsync(view);

        Assert.False(mounted.View.IsFirstPaneCollapsed);
        Assert.True(mounted.View.IsSecondPaneCollapsed);
        var firstHost = FindPart<ContentControl>(mounted.View, "PART_FirstContentHost");
        Assert.True(firstHost.IsVisible);
        Assert.Same(view.FirstContent, firstHost.Content);
        var secondHost = FindPart<ContentControl>(mounted.View, "PART_SecondContentHost");
        Assert.False(secondHost.IsVisible);
        Assert.Null(secondHost.Content);
        var header = FindPart<Button>(mounted.View, "PART_SecondCollapsedHeader");
        Assert.True(header.IsVisible);
        Assert.IsType<PathIcon>(header.Content);
        Assert.False(FindPart<Border>(mounted.View, "PART_SplitHandle").IsVisible);
        var (first, second) = GetPaneLengths(mounted.View);
        Assert.Equal(GridUnitType.Star, first.GridUnitType);
        Assert.Equal(34, second.Value);
        Assert.Equal(GridUnitType.Pixel, second.GridUnitType);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_IgnoresCollapsedState_WhenPaneCannotCollapse()
    {
        var view = CreateView();
        view.CanCollapseFirstPane = false;
        view.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference("first", 0.2, IsCollapsed: true),
            new SessionPaneSizePreference("second", 0.8),
        ]);

        await using var mounted = await MountAsync(view);

        Assert.False(mounted.View.IsFirstPaneCollapsed);
        Assert.True(FindPart<ContentControl>(mounted.View, "PART_FirstContentHost").IsVisible);
        Assert.True(FindPart<Border>(mounted.View, "PART_SplitHandle").IsVisible);
        var (first, second) = GetPaneLengths(mounted.View);
        Assert.Equal(0.2, first.Value);
        Assert.Equal(0.8, second.Value);
        Assert.Equal(GridUnitType.Star, first.GridUnitType);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_UsesDefaults_WhenPaneIdsDoNotMatch()
    {
        var view = CreateView();
        view.DefaultFirstLength = new GridLength(2, GridUnitType.Star);
        view.DefaultSecondLength = new GridLength(1, GridUnitType.Star);
        view.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference("unknown", 0.25),
            new SessionPaneSizePreference("second", 0.75),
        ]);

        await using var mounted = await MountAsync(view);

        var (first, second) = GetPaneLengths(mounted.View);
        Assert.Equal(2, first.Value);
        Assert.Equal(1, second.Value);
        Assert.Equal(GridUnitType.Star, first.GridUnitType);
        Assert.Equal(GridUnitType.Star, second.GridUnitType);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_UsesDefaults_WhenStoredExpandedRatioIsAtCollapseThreshold()
    {
        var view = CreateView();
        view.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference("first", 0.05),
            new SessionPaneSizePreference("second", 0.95),
        ]);

        await using var mounted = await MountAsync(view);

        Assert.False(mounted.View.IsFirstPaneCollapsed);
        Assert.False(mounted.View.IsSecondPaneCollapsed);
        var (first, second) = GetPaneLengths(mounted.View);
        Assert.Equal(1, first.Value);
        Assert.Equal(1, second.Value);
        Assert.Equal(GridUnitType.Star, first.GridUnitType);
        Assert.Equal(GridUnitType.Star, second.GridUnitType);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_HidesHandle_WhenOnlyOnePaneIsPresent()
    {
        var view = CreateView();
        view.HasSecondPane = false;

        await using var mounted = await MountAsync(view);

        Assert.True(FindPart<ContentControl>(mounted.View, "PART_FirstContentHost").IsVisible);
        Assert.False(FindPart<ContentControl>(mounted.View, "PART_SecondContentHost").IsVisible);
        Assert.False(FindPart<Border>(mounted.View, "PART_SplitHandle").IsVisible);
        var (first, second) = GetPaneLengths(mounted.View);
        Assert.Equal(1, first.Value);
        Assert.Equal(GridUnitType.Star, first.GridUnitType);
        Assert.Equal(0, second.Value);
        Assert.Equal(GridUnitType.Pixel, second.GridUnitType);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_ClickingCollapsedHeader_RestoresStoredPaneRatio()
    {
        var view = CreateView();
        view.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference("first", 0.2, IsCollapsed: true),
            new SessionPaneSizePreference("second", 0.8),
        ]);

        await using var mounted = await MountAsync(view);

        var header = FindPart<Button>(mounted.View, "PART_FirstCollapsedHeader");
        header.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(mounted.View.IsFirstPaneCollapsed);
        var firstHost = FindPart<ContentControl>(mounted.View, "PART_FirstContentHost");
        Assert.True(firstHost.IsVisible);
        Assert.Same(view.FirstContent, firstHost.Content);
        Assert.NotNull(mounted.View.Preferences);
        AssertPane(mounted.View.Preferences!, "first", 0.2, isCollapsed: false);
        AssertPane(mounted.View.Preferences!, "second", 0.8, isCollapsed: false);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_DoubleTapHandle_ResetsToDefaultsAndClearsCollapse()
    {
        var view = CreateView();
        view.DefaultFirstLength = new GridLength(2, GridUnitType.Star);
        view.DefaultSecondLength = new GridLength(1, GridUnitType.Star);
        view.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference("first", 0.2, IsCollapsed: true),
            new SessionPaneSizePreference("second", 0.8),
        ]);

        await using var mounted = await MountAsync(view);

        mounted.View.ResetToDefaultsForTests();

        Assert.False(mounted.View.IsFirstPaneCollapsed);
        Assert.False(mounted.View.IsSecondPaneCollapsed);
        Assert.NotNull(mounted.View.Preferences);
        AssertPane(mounted.View.Preferences!, "first", 2.0 / 3.0, isCollapsed: false);
        AssertPane(mounted.View.Preferences!, "second", 1.0 / 3.0, isCollapsed: false);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_DragBelowThreshold_ShowsPreviewAndCommitsCollapsedState()
    {
        var view = CreateView();
        await using var mounted = await MountAsync(view);

        mounted.View.BeginDragForTests();
        mounted.View.DragToFirstRatioForTests(0.04);

        Assert.True(mounted.View.IsDragging);
        Assert.True(mounted.View.IsFirstPaneCollapsed);
        Assert.Null(mounted.View.Preferences);

        mounted.View.CompleteDragForTests();

        Assert.False(mounted.View.IsDragging);
        Assert.NotNull(mounted.View.Preferences);
        AssertPane(mounted.View.Preferences!, "first", 0.5, isCollapsed: true);
        AssertPane(mounted.View.Preferences!, "second", 0.5, isCollapsed: false);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_DragPastPaneBounds_KeepsCollapsedState()
    {
        var view = CreateView();
        await using var mounted = await MountAsync(view);

        mounted.View.BeginDragForTests();
        mounted.View.DragToFirstRatioForTests(0.04);
        Assert.True(mounted.View.IsFirstPaneCollapsed);

        mounted.View.DragToFirstRatioForTests(-0.2);
        Assert.True(mounted.View.IsFirstPaneCollapsed);

        mounted.View.CompleteDragForTests();

        Assert.NotNull(mounted.View.Preferences);
        AssertPane(mounted.View.Preferences!, "first", 0.5, isCollapsed: true);
        AssertPane(mounted.View.Preferences!, "second", 0.5, isCollapsed: false);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_DragPastSecondPaneBounds_KeepsCollapsedState()
    {
        var view = CreateView();
        await using var mounted = await MountAsync(view);

        mounted.View.BeginDragForTests();
        mounted.View.DragToFirstRatioForTests(0.96);
        Assert.True(mounted.View.IsSecondPaneCollapsed);

        mounted.View.DragToFirstRatioForTests(1.2);
        Assert.True(mounted.View.IsSecondPaneCollapsed);

        mounted.View.CompleteDragForTests();

        Assert.NotNull(mounted.View.Preferences);
        AssertPane(mounted.View.Preferences!, "first", 0.5, isCollapsed: false);
        AssertPane(mounted.View.Preferences!, "second", 0.5, isCollapsed: true);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_ClickingCollapsedHeader_AfterDragCollapseRestoresDragStartRatio()
    {
        var view = CreateView();
        view.Preferences = new SessionPaneGroupPreferences(
        [
            new SessionPaneSizePreference("first", 0.35),
            new SessionPaneSizePreference("second", 0.65),
        ]);

        await using var mounted = await MountAsync(view);

        mounted.View.BeginDragForTests();
        mounted.View.DragToFirstRatioForTests(0.04);
        mounted.View.CompleteDragForTests();

        Assert.NotNull(mounted.View.Preferences);
        AssertPane(mounted.View.Preferences!, "first", 0.35, isCollapsed: true);
        AssertPane(mounted.View.Preferences!, "second", 0.65, isCollapsed: false);

        FindPart<Button>(mounted.View, "PART_FirstCollapsedHeader")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.NotNull(mounted.View.Preferences);
        AssertPane(mounted.View.Preferences!, "first", 0.35, isCollapsed: false);
        AssertPane(mounted.View.Preferences!, "second", 0.65, isCollapsed: false);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_DragBackAboveThreshold_ClearsPreviewBeforeCommit()
    {
        var view = CreateView();
        await using var mounted = await MountAsync(view);

        mounted.View.BeginDragForTests();
        mounted.View.DragToFirstRatioForTests(0.04);
        Assert.True(mounted.View.IsFirstPaneCollapsed);

        mounted.View.DragToFirstRatioForTests(0.35);
        Assert.False(mounted.View.IsFirstPaneCollapsed);
        Assert.True(FindPart<ContentControl>(mounted.View, "PART_FirstContentHost").IsVisible);

        mounted.View.CompleteDragForTests();

        Assert.NotNull(mounted.View.Preferences);
        AssertPane(mounted.View.Preferences!, "first", 0.35, isCollapsed: false);
        AssertPane(mounted.View.Preferences!, "second", 0.65, isCollapsed: false);
    }

    [AvaloniaFact]
    public async Task CollapsibleSplitView_FiniteResize_DoesNotExpandToOversizedChildDesiredWidth()
    {
        var view = CreateView();
        view.DefaultFirstLength = new GridLength(1, GridUnitType.Star);
        view.DefaultSecondLength = new GridLength(400);
        view.FirstContent = new Border { MinWidth = 1600 };

        await using var mounted = await MountAsync(view);

        Resize(mounted.View, width: 1800, height: 700);
        Resize(mounted.View, width: 900, height: 700);

        Assert.Equal(900, mounted.View.DesiredSize.Width);
        Assert.Equal(900, mounted.View.Bounds.Width);
        var grid = Assert.IsType<Grid>(mounted.View.Content);

        Assert.True(
            grid.ColumnDefinitions[2].ActualWidth > 0,
            $"Expected second pane to keep positive width. Column widths: {grid.ColumnDefinitions[0].ActualWidth}, {grid.ColumnDefinitions[2].ActualWidth}.");
    }

    private static CollapsibleSplitView CreateView()
    {
        return new CollapsibleSplitView
        {
            FirstPaneId = "first",
            SecondPaneId = "second",
            FirstPaneLabel = "First",
            SecondPaneLabel = "Second",
            FirstContent = new Border(),
            SecondContent = new Border(),
        };
    }

    private static async Task<MountedCollapsibleSplitView> MountAsync(CollapsibleSplitView view)
    {
        ViewTestHelpers.EnsureViewTestResources();
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedCollapsibleSplitView(host, view);
    }

    private static (GridLength First, GridLength Second) GetPaneLengths(CollapsibleSplitView view)
    {
        var grid = Assert.IsType<Grid>(view.Content);
        if (view.Orientation == Orientation.Horizontal)
        {
            return (grid.ColumnDefinitions[0].Width, grid.ColumnDefinitions[2].Width);
        }

        return (grid.RowDefinitions[0].Height, grid.RowDefinitions[2].Height);
    }

    private static T FindPart<T>(CollapsibleSplitView view, string name)
        where T : Control
    {
        return view.GetVisualDescendants()
            .OfType<T>()
            .Single(control => control.Name == name);
    }

    private static void AssertPane(
        SessionPaneGroupPreferences preferences,
        string paneId,
        double ratio,
        bool isCollapsed)
    {
        var pane = Assert.Single(preferences.Panes, pane => pane.PaneId == paneId);
        Assert.Equal(ratio, pane.Ratio, precision: 6);
        Assert.Equal(isCollapsed, pane.IsCollapsed);
    }

    private static void Resize(Control control, double width, double height)
    {
        control.Measure(new Avalonia.Size(width, height));
        control.Arrange(new Avalonia.Rect(0, 0, width, height));
    }
}

internal sealed class MountedCollapsibleSplitView(Window host, CollapsibleSplitView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public CollapsibleSplitView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Sufni.App.DesktopViews.Controls;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Models;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Views.Editors;

[Collection("Ui")]
public class SessionDetailDesktopShellTests
{
    [AvaloniaFact]
    public async Task SessionShellDesktopView_UpdatesOptionalRegions_WhenOptionalContentChanges()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var shell = CreateShell();
        shell.ControlContent = new Border();

        await using var mounted = await MountAsync(shell);

        var graphMediaSplit = FindSplit(mounted.View, "GraphMediaSplit");
        var controlHost = mounted.View.FindControl<ContentControl>("ControlHost");
        var controlSplitter = mounted.View.FindControl<GridSplitter>("ControlSplitter");

        Assert.NotNull(controlHost);
        Assert.NotNull(controlSplitter);
        Assert.True(graphMediaSplit.HasSecondPane);
        Assert.True(FindPart<Border>(graphMediaSplit, "PART_SplitHandle").IsVisible);
        Assert.True(controlSplitter!.IsVisible);

        shell.HasMediaContent = false;
        shell.ControlContent = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        var (_, mediaLength) = GetPaneLengths(graphMediaSplit);
        Assert.False(graphMediaSplit.HasSecondPane);
        Assert.False(FindPart<Border>(graphMediaSplit, "PART_SplitHandle").IsVisible);
        Assert.False(FindPart<ContentControl>(graphMediaSplit, "PART_SecondContentHost").IsVisible);
        Assert.Equal(0, mediaLength.Value);
        Assert.Null(controlHost!.Content);
        Assert.False(controlSplitter.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionShellDesktopView_AppliesStoredPaneRatios()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var shell = CreateShell();
        shell.LayoutPreferences = new SessionLayoutPreferences(
            desktopShellRows: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.GraphMediaArea, 0.6),
                new SessionPaneSizePreference(SessionLayoutPaneIds.StatisticsSidebarArea, 0.4),
            ]),
            desktopGraphMediaColumns: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Graph, 0.7),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.3),
            ]),
            desktopStatisticsSidebarColumns: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Statistics, 0.65),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Sidebar, 0.35),
            ]));

        await using var mounted = await MountAsync(shell);

        var shellRows = GetPaneLengths(FindSplit(mounted.View, "ShellRowsSplit"));
        var graphMedia = GetPaneLengths(FindSplit(mounted.View, "GraphMediaSplit"));
        var statisticsSidebar = GetPaneLengths(FindSplit(mounted.View, "StatisticsSidebarSplit"));

        Assert.Equal(0.6, shellRows.First.Value);
        Assert.Equal(0.4, shellRows.Second.Value);
        Assert.Equal(GridUnitType.Star, shellRows.First.GridUnitType);
        Assert.Equal(GridUnitType.Star, shellRows.Second.GridUnitType);
        Assert.Equal(0.7, graphMedia.First.Value);
        Assert.Equal(0.3, graphMedia.Second.Value);
        Assert.Equal(0.65, statisticsSidebar.First.Value);
        Assert.Equal(0.35, statisticsSidebar.Second.Value);
    }

    [AvaloniaFact]
    public async Task SessionShellDesktopView_UsesDefaults_WhenStoredPaneSetDoesNotMatch()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var shell = CreateShell();
        shell.LayoutPreferences = new SessionLayoutPreferences(
            desktopGraphMediaColumns: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference("unknown", 0.7),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.3),
            ]));

        await using var mounted = await MountAsync(shell);

        var (graph, media) = GetPaneLengths(FindSplit(mounted.View, "GraphMediaSplit"));

        Assert.Equal(1, graph.Value);
        Assert.Equal(GridUnitType.Star, graph.GridUnitType);
        Assert.Equal(400, media.Value);
        Assert.Equal(GridUnitType.Pixel, media.GridUnitType);
    }

    [AvaloniaFact]
    public async Task SessionShellDesktopView_UsesMediaColumnWidthAsDefaultMediaLength()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var shell = CreateShell();
        shell.MediaColumnWidth = 480;

        await using var mounted = await MountAsync(shell);

        var (_, media) = GetPaneLengths(FindSplit(mounted.View, "GraphMediaSplit"));

        Assert.Equal(480, media.Value);
        Assert.Equal(GridUnitType.Pixel, media.GridUnitType);
    }

    [AvaloniaFact]
    public async Task SessionShellDesktopView_RendersCollapsedHeaders_ForStoredCollapsedStates()
    {
        await AssertCollapsedPaneAsync(
            new SessionLayoutPreferences(
                desktopShellRows: new SessionPaneGroupPreferences(
                [
                    new SessionPaneSizePreference(SessionLayoutPaneIds.GraphMediaArea, 0.2, IsCollapsed: true),
                    new SessionPaneSizePreference(SessionLayoutPaneIds.StatisticsSidebarArea, 0.8),
                ])),
            "ShellRowsSplit",
            firstPane: true);

        await AssertCollapsedPaneAsync(
            new SessionLayoutPreferences(
                desktopShellRows: new SessionPaneGroupPreferences(
                [
                    new SessionPaneSizePreference(SessionLayoutPaneIds.GraphMediaArea, 0.8),
                    new SessionPaneSizePreference(SessionLayoutPaneIds.StatisticsSidebarArea, 0.2, IsCollapsed: true),
                ])),
            "ShellRowsSplit",
            firstPane: false);

        await AssertCollapsedPaneAsync(
            new SessionLayoutPreferences(
                desktopGraphMediaColumns: new SessionPaneGroupPreferences(
                [
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Graph, 0.2, IsCollapsed: true),
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.8),
                ])),
            "GraphMediaSplit",
            firstPane: true);

        await AssertCollapsedPaneAsync(
            new SessionLayoutPreferences(
                desktopGraphMediaColumns: new SessionPaneGroupPreferences(
                [
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Graph, 0.8),
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.2, IsCollapsed: true),
                ])),
            "GraphMediaSplit",
            firstPane: false);

        await AssertCollapsedPaneAsync(
            new SessionLayoutPreferences(
                desktopStatisticsSidebarColumns: new SessionPaneGroupPreferences(
                [
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Statistics, 0.2, IsCollapsed: true),
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Sidebar, 0.8),
                ])),
            "StatisticsSidebarSplit",
            firstPane: true);

        await AssertCollapsedPaneAsync(
            new SessionLayoutPreferences(
                desktopStatisticsSidebarColumns: new SessionPaneGroupPreferences(
                [
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Statistics, 0.8),
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Sidebar, 0.2, IsCollapsed: true),
                ])),
            "StatisticsSidebarSplit",
            firstPane: false);
    }

    [AvaloniaFact]
    public async Task SessionShellDesktopView_StatisticsAndSidebarCollapsedHeaders_RestoreStoredPaneRatios()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var shell = CreateShell();
        shell.LayoutPreferences = new SessionLayoutPreferences(
            desktopStatisticsSidebarColumns: new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Statistics, 0.2, IsCollapsed: true),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Sidebar, 0.8),
            ]));

        await using var mounted = await MountAsync(shell);

        var split = FindSplit(mounted.View, "StatisticsSidebarSplit");
        FindPart<Button>(split, "PART_FirstCollapsedHeader")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await ViewTestHelpers.FlushDispatcherAsync();

        AssertPane(
            mounted.View.LayoutPreferences.DesktopStatisticsSidebarColumns!,
            SessionLayoutPaneIds.Statistics,
            0.2,
            isCollapsed: false);
        AssertPane(
            mounted.View.LayoutPreferences.DesktopStatisticsSidebarColumns!,
            SessionLayoutPaneIds.Sidebar,
            0.8,
            isCollapsed: false);

        mounted.View.LayoutPreferences = mounted.View.LayoutPreferences with
        {
            DesktopStatisticsSidebarColumns = new SessionPaneGroupPreferences(
            [
                new SessionPaneSizePreference(SessionLayoutPaneIds.Statistics, 0.8),
                new SessionPaneSizePreference(SessionLayoutPaneIds.Sidebar, 0.2, IsCollapsed: true),
            ]),
        };
        await ViewTestHelpers.FlushDispatcherAsync();

        FindPart<Button>(split, "PART_SecondCollapsedHeader")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await ViewTestHelpers.FlushDispatcherAsync();

        AssertPane(
            mounted.View.LayoutPreferences.DesktopStatisticsSidebarColumns!,
            SessionLayoutPaneIds.Statistics,
            0.8,
            isCollapsed: false);
        AssertPane(
            mounted.View.LayoutPreferences.DesktopStatisticsSidebarColumns!,
            SessionLayoutPaneIds.Sidebar,
            0.2,
            isCollapsed: false);
    }

    private static async Task AssertCollapsedPaneAsync(
        SessionLayoutPreferences layout,
        string splitName,
        bool firstPane)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var shell = CreateShell();
        shell.LayoutPreferences = layout;

        await using var mounted = await MountAsync(shell);

        var split = FindSplit(mounted.View, splitName);
        var collapsedHostName = firstPane ? "PART_FirstContentHost" : "PART_SecondContentHost";
        var expandedHostName = firstPane ? "PART_SecondContentHost" : "PART_FirstContentHost";
        var headerName = firstPane ? "PART_FirstCollapsedHeader" : "PART_SecondCollapsedHeader";
        var collapsedHost = FindPart<ContentControl>(split, collapsedHostName);
        var expandedHost = FindPart<ContentControl>(split, expandedHostName);

        Assert.Equal(firstPane, split.IsFirstPaneCollapsed);
        Assert.Equal(!firstPane, split.IsSecondPaneCollapsed);
        Assert.False(collapsedHost.IsVisible);
        Assert.Null(collapsedHost.Content);
        Assert.True(expandedHost.IsVisible);
        Assert.NotNull(expandedHost.Content);
        Assert.True(FindPart<Button>(split, headerName).IsVisible);
    }

    private static SessionShellDesktopView CreateShell()
    {
        return new SessionShellDesktopView
        {
            GraphContent = new Border(),
            HasMediaContent = true,
            MediaContent = new Border(),
            StatisticsContent = new Border(),
            SidebarContent = new Border(),
        };
    }

    private static async Task<MountedSessionShellDesktopView> MountAsync(SessionShellDesktopView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionShellDesktopView(host, view);
    }

    private static CollapsibleSplitView FindSplit(SessionShellDesktopView view, string name)
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
}

internal sealed class MountedSessionShellDesktopView(Window host, SessionShellDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public SessionShellDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Views.Editors;

public class SessionDetailDesktopShellTests
{
    [AvaloniaFact]
    public async Task SessionShellDesktopView_BindsRegionHostsToShellContentProperties()
    {
        var graph = new Border();
        var media = new Border();
        var statistics = new Border();
        var controls = new Border();
        var sidebar = new Border();

        var shell = new SessionShellDesktopView
        {
            GraphContent = graph,
            MediaContent = media,
            StatisticsContent = statistics,
            ControlContent = controls,
            SidebarContent = sidebar,
        };

        await using var mounted = await MountAsync(shell);

        var graphHost = mounted.View.FindControl<ContentControl>("GraphHost");
        var mediaHost = mounted.View.FindControl<ContentControl>("MediaHost");
        var statisticsHost = mounted.View.FindControl<ContentControl>("StatisticsHost");
        var controlHost = mounted.View.FindControl<ContentControl>("ControlHost");
        var sidebarHost = mounted.View.FindControl<ContentControl>("SidebarHost");

        Assert.NotNull(graphHost);
        Assert.NotNull(mediaHost);
        Assert.NotNull(statisticsHost);
        Assert.NotNull(controlHost);
        Assert.NotNull(sidebarHost);
        Assert.Same(graph, graphHost!.Content);
        Assert.Same(media, mediaHost!.Content);
        Assert.Same(statistics, statisticsHost!.Content);
        Assert.Same(controls, controlHost!.Content);
        Assert.Same(sidebar, sidebarHost!.Content);
    }

    [AvaloniaFact]
    public async Task SessionShellDesktopView_UpdatesOptionalRegions_WhenOptionalContentChanges()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var shell = new SessionShellDesktopView
        {
            GraphContent = new Border(),
            HasMediaContent = true,
            MediaContent = new Border(),
            StatisticsContent = new Border(),
            ControlContent = new Border(),
            SidebarContent = new Border(),
        };

        await using var mounted = await MountAsync(shell);

        var mediaHost = mounted.View.FindControl<ContentControl>("MediaHost");
        var controlHost = mounted.View.FindControl<ContentControl>("ControlHost");
        var mediaSplitter = mounted.View.FindControl<GridSplitter>("MediaSplitter");
        var controlSplitter = mounted.View.FindControl<GridSplitter>("ControlSplitter");
        var topLayoutGrid = mounted.View.FindControl<Grid>("TopLayoutGrid");

        Assert.NotNull(mediaHost);
        Assert.NotNull(controlHost);
        Assert.NotNull(mediaSplitter);
        Assert.NotNull(controlSplitter);
        Assert.NotNull(topLayoutGrid);
        Assert.True(mediaSplitter!.IsVisible);
        Assert.True(controlSplitter!.IsVisible);

        shell.HasMediaContent = false;
        shell.ControlContent = null;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.NotNull(mediaHost!.Content);
        Assert.Null(controlHost!.Content);
        Assert.False(mediaSplitter.IsVisible);
        Assert.False(controlSplitter.IsVisible);
        Assert.False(mediaHost.IsVisible);
        Assert.Equal(0, topLayoutGrid!.ColumnDefinitions[2].Width.Value);
    }

    [AvaloniaFact]
    public async Task SessionShellDesktopView_ResetSectionSplitters_RestoresDefaultSectionSizes()
    {
        var shell = new SessionShellDesktopView
        {
            GraphContent = new Border(),
            HasMediaContent = true,
            MediaContent = new Border(),
            StatisticsContent = new Border(),
            ControlContent = new Border(),
            SidebarContent = new Border(),
        };

        await using var mounted = await MountAsync(shell);

        var rootLayoutGrid = mounted.View.FindControl<Grid>("RootLayoutGrid");
        var topLayoutGrid = mounted.View.FindControl<Grid>("TopLayoutGrid");
        var bottomLayoutGrid = mounted.View.FindControl<Grid>("BottomLayoutGrid");

        Assert.NotNull(rootLayoutGrid);
        Assert.NotNull(topLayoutGrid);
        Assert.NotNull(bottomLayoutGrid);

        rootLayoutGrid!.RowDefinitions[0].Height = new GridLength(3, GridUnitType.Star);
        rootLayoutGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
        topLayoutGrid!.ColumnDefinitions[0].Width = new GridLength(2, GridUnitType.Star);
        topLayoutGrid.ColumnDefinitions[2].Width = new GridLength(700, GridUnitType.Pixel);
        bottomLayoutGrid!.ColumnDefinitions[0].Width = new GridLength(2, GridUnitType.Star);
        bottomLayoutGrid.ColumnDefinitions[2].Width = new GridLength(650, GridUnitType.Pixel);

        SessionSectionGridSizing.ResetRows(
            rootLayoutGrid,
            (0, new GridLength(1, GridUnitType.Star)),
            (2, new GridLength(1, GridUnitType.Star)));
        SessionSectionGridSizing.ResetColumns(
            topLayoutGrid,
            (0, new GridLength(1, GridUnitType.Star)),
            (2, GridLength.Auto));
        SessionSectionGridSizing.ResetColumns(
            bottomLayoutGrid,
            (0, new GridLength(1, GridUnitType.Star)),
            (2, new GridLength(400, GridUnitType.Pixel)));

        Assert.Equal(new GridLength(1, GridUnitType.Star), rootLayoutGrid.RowDefinitions[0].Height);
        Assert.Equal(new GridLength(1, GridUnitType.Star), rootLayoutGrid.RowDefinitions[2].Height);
        Assert.Equal(new GridLength(1, GridUnitType.Star), topLayoutGrid.ColumnDefinitions[0].Width);
        Assert.Equal(GridLength.Auto, topLayoutGrid.ColumnDefinitions[2].Width);
        Assert.Equal(new GridLength(1, GridUnitType.Star), bottomLayoutGrid.ColumnDefinitions[0].Width);
        Assert.Equal(new GridLength(400, GridUnitType.Pixel), bottomLayoutGrid.ColumnDefinitions[2].Width);
    }

    private static async Task<MountedSessionShellDesktopView> MountAsync(SessionShellDesktopView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionShellDesktopView(host, view);
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

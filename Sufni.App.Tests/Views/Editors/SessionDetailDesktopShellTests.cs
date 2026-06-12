using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
    public async Task SessionShellDesktopView_AppliesStoredPaneRatios()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var shell = new SessionShellDesktopView
        {
            GraphContent = new Border(),
            HasMediaContent = true,
            MediaContent = new Border(),
            StatisticsContent = new Border(),
            SidebarContent = new Border(),
            LayoutPreferences = new SessionLayoutPreferences(
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
                ])),
        };

        await using var mounted = await MountAsync(shell);

        var rootGrid = mounted.View.FindControl<Grid>("RootLayoutGrid")!;
        var topGrid = mounted.View.FindControl<Grid>("TopLayoutGrid")!;
        var bottomGrid = mounted.View.FindControl<Grid>("BottomLayoutGrid")!;

        Assert.Equal(0.6, rootGrid.RowDefinitions[0].Height.Value);
        Assert.Equal(GridUnitType.Star, rootGrid.RowDefinitions[0].Height.GridUnitType);
        Assert.Equal(0.4, rootGrid.RowDefinitions[2].Height.Value);
        Assert.Equal(GridUnitType.Star, rootGrid.RowDefinitions[2].Height.GridUnitType);
        Assert.Equal(0.7, topGrid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(0.3, topGrid.ColumnDefinitions[2].Width.Value);
        Assert.Equal(0.65, bottomGrid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(0.35, bottomGrid.ColumnDefinitions[2].Width.Value);
    }

    [AvaloniaFact]
    public async Task SessionShellDesktopView_UsesDefaults_WhenStoredPaneSetDoesNotMatch()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var shell = new SessionShellDesktopView
        {
            GraphContent = new Border(),
            HasMediaContent = true,
            MediaContent = new Border(),
            StatisticsContent = new Border(),
            SidebarContent = new Border(),
            LayoutPreferences = new SessionLayoutPreferences(
                desktopGraphMediaColumns: new SessionPaneGroupPreferences(
                [
                    new SessionPaneSizePreference("unknown", 0.7),
                    new SessionPaneSizePreference(SessionLayoutPaneIds.Media, 0.3),
                ])),
        };

        await using var mounted = await MountAsync(shell);

        var topGrid = mounted.View.FindControl<Grid>("TopLayoutGrid")!;

        Assert.Equal(1, topGrid.ColumnDefinitions[0].Width.Value);
        Assert.Equal(GridUnitType.Star, topGrid.ColumnDefinitions[0].Width.GridUnitType);
        Assert.Equal(GridUnitType.Auto, topGrid.ColumnDefinitions[2].Width.GridUnitType);
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

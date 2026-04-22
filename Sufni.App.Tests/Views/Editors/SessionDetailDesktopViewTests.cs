using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.DesktopViews.Items;

namespace Sufni.App.Tests.Views.Editors;

public class SessionDetailDesktopViewTests
{
    [AvaloniaFact]
    public async Task SessionDetailDesktopView_ComposesRecordedSessionRegionsIntoShellHosts()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountDesktopAsync(
            loadResult: context.CreateDesktopLoadedState(includeImu: true));

        var shell = mounted.View.GetVisualDescendants().OfType<SessionShellDesktopView>().Single();
        var graphHost = shell.FindControl<ContentControl>("GraphHost");
        var mediaHost = shell.FindControl<ContentControl>("MediaHost");
        var statisticsHost = shell.FindControl<ContentControl>("StatisticsHost");
        var controlHost = shell.FindControl<ContentControl>("ControlHost");
        var sidebarHost = shell.FindControl<ContentControl>("SidebarHost");

        var graphView = Assert.IsType<RecordedSessionGraphDesktopView>(shell.GraphContent);
        var mediaView = Assert.IsType<SessionMediaDesktopView>(shell.MediaContent);
        var statisticsView = Assert.IsType<SessionStatisticsDesktopView>(shell.StatisticsContent);
        var sidebarView = Assert.IsType<SessionSidebarDesktopView>(shell.SidebarContent);

        Assert.NotNull(graphHost);
        Assert.NotNull(mediaHost);
        Assert.NotNull(statisticsHost);
        Assert.NotNull(controlHost);
        Assert.NotNull(sidebarHost);

        Assert.Same(graphView, graphHost!.Content);
        Assert.Same(mediaView, mediaHost!.Content);
        Assert.Same(statisticsView, statisticsHost!.Content);
        Assert.Null(controlHost!.Content);
        Assert.Same(sidebarView, sidebarHost!.Content);

        Assert.Same(mounted.Editor, graphView.DataContext);
        Assert.Same(mounted.Editor, mediaView.DataContext);
        Assert.Same(mounted.Editor, statisticsView.DataContext);
        Assert.Same(mounted.Editor, sidebarView.DataContext);
    }
}
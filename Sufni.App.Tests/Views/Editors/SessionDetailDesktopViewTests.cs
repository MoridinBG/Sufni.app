using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ScottPlot.Plottables;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.DesktopViews.Items;
using Sufni.App.DesktopViews.Plots;
using Sufni.App.SessionDetails;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Tests.Views.Plots;
using Sufni.App.Views.Controls;

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

    [AvaloniaFact]
    public async Task SessionDetailDesktopView_LoadsRecordedPlots_AfterDesktopTelemetryArrives()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountDesktopAsync(
            loadResult: context.CreateDesktopLoadedState(includeImu: true));

        var travelView = mounted.View.GetVisualDescendants().OfType<TravelPlotDesktopView>().Single();
        var velocityView = mounted.View.GetVisualDescendants().OfType<VelocityPlotDesktopView>().Single();
        var imuView = mounted.View.GetVisualDescendants().OfType<ImuPlotDesktopView>().Single();

        var travelPlot = PlotViewTestSupport.GetRenderedPlot(travelView);
        var velocityPlot = PlotViewTestSupport.GetRenderedPlot(velocityView);
        var imuPlot = PlotViewTestSupport.GetRenderedPlot(imuView);

        Assert.Equal(2, travelPlot.Plot.PlottableList.OfType<Signal>().Count());
        Assert.Equal(2, velocityPlot.Plot.PlottableList.OfType<Signal>().Count());
        Assert.NotEmpty(imuPlot.Plot.PlottableList.OfType<Signal>());
    }

    [AvaloniaFact]
    public async Task SessionDetailDesktopView_ShowsGraphPlaceholders_WhenDesktopTelemetryIsPending()
    {
        var context = new SessionDetailViewTestContext();
        var snapshot = context.CreateTelemetryBearingSnapshot(hasProcessedData: true);

        await using var mounted = await context.MountDesktopAsync(
            snapshot: snapshot,
            loadResult: new SessionDesktopLoadResult.TelemetryPending());

        var graphView = mounted.View.GetVisualDescendants().OfType<RecordedSessionGraphDesktopView>().Single();
        var graphHosts = graphView.GetVisualDescendants().OfType<PlaceholderOverlayContainer>().ToArray();
        var progressIndicators = graphView.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.Name == "ProgressIndicator")
            .ToArray();

        Assert.Equal(2, graphHosts.Length);
        Assert.All(graphHosts, host => Assert.True(host.IsVisible));
        Assert.Equal(2, progressIndicators.Length);
        Assert.All(progressIndicators, indicator => Assert.True(indicator.IsVisible));
    }

    [AvaloniaFact]
    public async Task SessionDetailDesktopView_ReplacesShellWithScreenError_WhenDesktopLoadFails()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountDesktopAsync(
            loadResult: new SessionDesktopLoadResult.Failed("boom"));

        var shell = mounted.View.GetVisualDescendants().OfType<SessionShellDesktopView>().Single();
        var errorText = mounted.View.FindControl<TextBlock>("ScreenErrorText");

        Assert.NotNull(errorText);
        Assert.False(shell.IsVisible);
        Assert.Equal("Could not load session data: boom", errorText!.Text);
    }
}
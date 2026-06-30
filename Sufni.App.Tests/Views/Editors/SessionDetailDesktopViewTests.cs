using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Tests.TestSupport;

using Sufni.App.Sessions.Detail.DesktopViews.Editors;
using Sufni.App.Sessions.Detail.DesktopViews.Items;
using Sufni.App.Sessions.Graph.DesktopViews.Items;
using Sufni.App.Sessions.Media.DesktopViews.Items;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Statistics.DesktopViews.Items;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Shared.Views.Overlays;
namespace Sufni.App.Tests.Views.Editors;

[Collection("Ui")]
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
        var errorMessagesBar = mounted.View.FindControl<ErrorMessagesBar>("SessionErrorMessagesBar");

        var graphView = Assert.IsType<RecordedSessionGraphDesktopView>(shell.GraphContent);
        var mediaView = Assert.IsType<SessionMediaDesktopView>(shell.MediaContent);
        var statisticsView = Assert.IsType<SessionStatisticsDesktopView>(shell.StatisticsContent);
        var sidebarView = Assert.IsType<SessionSidebarDesktopView>(shell.SidebarContent);

        Assert.NotNull(graphHost);
        Assert.NotNull(mediaHost);
        Assert.NotNull(statisticsHost);
        Assert.NotNull(controlHost);
        Assert.NotNull(sidebarHost);
        Assert.NotNull(errorMessagesBar);

        Assert.Same(graphView, graphHost!.Content);
        Assert.Same(mediaView, mediaHost!.Content);
        Assert.Same(statisticsView, statisticsHost!.Content);
        Assert.Null(controlHost!.Content);
        Assert.Same(sidebarView, sidebarHost!.Content);

        Assert.Same(mounted.Editor.GraphWorkspace, graphView.DataContext);
        Assert.Same(mounted.Editor.MediaWorkspace, mediaView.DataContext);
        Assert.Same(mounted.Editor.StatisticsWorkspace, statisticsView.DataContext);
        Assert.Same(mounted.Editor.SidebarWorkspace, sidebarView.DataContext);
        Assert.Same(mounted.Editor, errorMessagesBar!.DataContext);
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
        var graphHosts = graphView.GetVisualDescendants()
            .OfType<PlaceholderOverlayContainer>()
            .Where(host => host.IsVisible)
            .ToArray();
        var progressIndicators = graphView.GetVisualDescendants()
            .OfType<Control>()
            .Where(control => control.Name == "ProgressIndicator" && control.IsVisible)
            .ToArray();

        Assert.Equal(4, graphHosts.Length);
        Assert.Equal(4, progressIndicators.Length);
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
    }

    [AvaloniaFact]
    public async Task SessionDetailDesktopView_TakesKeyboardFocus_WhenLoaded()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountDesktopAsync(
            loadResult: context.CreateDesktopLoadedState());

        Assert.True(mounted.View.IsFocused);
    }
}

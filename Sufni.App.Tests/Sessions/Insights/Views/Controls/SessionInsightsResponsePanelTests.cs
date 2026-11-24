using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Sessions.Insights.Views.Controls;
using Sufni.App.Sessions.Models;
using Sufni.App.Terrain.Formatting;
using Sufni.App.Terrain.Views;
using Sufni.App.Tests.TestSupport.Harness;
using static Sufni.App.Tests.TestSupport.Fixtures.TestResponseRows;

namespace Sufni.App.Tests.Sessions.Insights.Views.Controls;

[Collection("Ui")]
public class SessionInsightsResponsePanelTests
{
    [AvaloniaFact]
    public async Task InsufficientBaselineShowsItsProgressWithoutAReport()
    {
        await using var mounted = await MountAsync(SessionResponsePanel.Hidden);
        var view = mounted.View;
        Assert.False(view.IsVisible);
        var insufficient = new SessionResponsePanel(SessionResponseState.InsufficientBaseline, "Your usual needs 5 rides on this bike: 3 of 5 so far.");

        view.DataContext = insufficient;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(view.IsVisible);
        Assert.Equal(insufficient.Description, view.FindControl<TextBlock>("Description")!.Text);
        Assert.False(view.FindControl<TerrainResponseReportView>("Report")!.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task ReportedPanelShowsItsReport()
    {
        var report = TerrainResponseReports.AgainstUsual([Row(null, 900)]);

        await using var mounted = await MountAsync(new SessionResponsePanel(SessionResponseState.Reported, "Your usual comes from 6 other rides.", report));

        var view = mounted.View.FindControl<TerrainResponseReportView>("Report")!;
        Assert.True(view.IsEffectivelyVisible);
        Assert.Same(report, view.DataContext);
    }

    private static Task<MountedView<SessionInsightsResponsePanel>> MountAsync(SessionResponsePanel panel)
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        return ViewTestHelpers.MountAsync(new SessionInsightsResponsePanel { DataContext = panel });
    }
}

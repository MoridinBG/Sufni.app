using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Sessions.Insights.Views.Controls;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Insights.Views.Controls;

[Collection("Ui")]
public class SessionInsightsResponsePanelTests
{
    [AvaloniaFact]
    public async Task InsufficientBaselineShowsItsCoverageWithoutRows()
    {
        await using var mounted = await MountAsync(SessionResponsePanel.Hidden);
        var view = mounted.View;
        Assert.False(view.IsVisible);
        var insufficient = new SessionResponsePanel(SessionResponseState.InsufficientBaseline,
            "Baseline coverage is 4 rides and 3,200 usable windows.", 4, 3200, []);

        view.DataContext = insufficient;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(view.IsVisible);
        Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == insufficient.Description);
        Assert.Empty(RenderedRows(view));
    }

    [AvaloniaFact]
    public async Task ReportedPanelShowsOneRowPerClass()
    {
        RoughnessClass?[] classes = [null, RoughnessClass.Calm, RoughnessClass.Medium, RoughnessClass.Rough];
        var reported = new SessionResponsePanel(SessionResponseState.Reported, "Ratios compare this ride with 5 other rides.", 5, 6000,
            [.. classes.Select(c => new SessionResponseRow(c?.ToString() ?? "All", "300 m", new TerrainResponseRow(c, 300, 0.25, 5, []), []))]);

        await using var mounted = await MountAsync(reported);

        Assert.Equal(classes, RenderedRows(mounted.View).Select(row => row.Measurement.Class));
    }

    private static Task<MountedView<SessionInsightsResponsePanel>> MountAsync(SessionResponsePanel panel)
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        return ViewTestHelpers.MountAsync(new SessionInsightsResponsePanel { DataContext = panel });
    }

    private static IEnumerable<SessionResponseRow> RenderedRows(Control view) =>
        view.GetVisualDescendants().OfType<StackPanel>().Select(panel => panel.DataContext).OfType<SessionResponseRow>().Distinct();
}

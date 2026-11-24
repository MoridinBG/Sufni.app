using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Terrain.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Views;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;
using static Sufni.App.Tests.TestSupport.Fixtures.TestResponseRows;

namespace Sufni.App.Tests.Terrain.Views;

[Collection("Ui")]
public class TerrainResponseReportViewTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task VerdictListsReplaceTheSummary_OnlyWhenThereAreVerdicts(bool finding)
    {
        var cell = finding ? Finding(ResponseTarget.FrontRms, 0.6) : Cell(ResponseTarget.FrontRms, 0.9);
        await using var mounted = await MountAsync(TerrainResponseReports.AgainstUsual([Row(null, 900, cell)]));
        var view = mounted.View;

        Assert.Equal(finding, view.FindControl<StackPanel>("ForkVerdicts")!.IsVisible);
        Assert.False(view.FindControl<StackPanel>("ShockVerdicts")!.IsVisible);
        Assert.Equal(!finding, view.FindControl<TextBlock>("Summary")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task EveryBarUsesTheSharedScaleOfTheReport()
    {
        var report = TerrainResponseReports.AgainstUsual([Row(null, 900, Cell(ResponseTarget.RearRms, 1.73))]);

        await using var mounted = await MountAsync(report);

        var bars = mounted.View.GetVisualDescendants().OfType<DeviationBar>().ToArray();
        Assert.Equal(6, bars.Length);
        Assert.All(bars, bar => Assert.Equal(report.Scale, bar.Scale));
    }

    private static Task<MountedView<TerrainResponseReportView>> MountAsync(TerrainResponseReport report)
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        return ViewTestHelpers.MountAsync(new TerrainResponseReportView { DataContext = report });
    }
}

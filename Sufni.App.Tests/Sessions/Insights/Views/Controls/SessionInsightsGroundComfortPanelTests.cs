using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Sessions.Insights.Views.Controls;
using Sufni.App.Terrain.Models;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Insights.Views.Controls;

[Collection("Ui")]
public class SessionInsightsGroundComfortPanelTests
{
    [AvaloniaFact]
    public async Task ColumnHeadingsLineUpWithTheirLevelValues()
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        var level = new GroundComfortLevel(RoughnessClass.Rough, "Rough", "1,2 km", 1,
            new("29 mm/s"), new("158 mm/s"), new("2,7 g"), null);
        var panel = new SessionInsightsGroundComfortPanel { DataContext = new GroundComfortReport([level], null, null) };
        await using var mounted = await ViewTestHelpers.MountAsync(panel);

        var levels = panel.FindControl<Grid>("Levels")!;
        var headings = (Grid)levels.Children[0];
        var row = levels.Children[1].GetVisualDescendants().OfType<Grid>().First();

        Assert.Equal(headings.ColumnDefinitions.Count, row.ColumnDefinitions.Count);
        for (var column = 0; column < row.ColumnDefinitions.Count; column++)
            Assert.Equal(Left(headings, column, panel), Left(row, column, panel), 1);
    }

    private static double Left(Grid grid, int column, Visual root) =>
        grid.Children.Single(child => Grid.GetColumn(child) == column).TranslatePoint(default, root)!.Value.X;
}

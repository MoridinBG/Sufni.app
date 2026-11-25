using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Views;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Views;

[Collection("Ui")]
public class GroundComfortViewTests
{
    [AvaloniaFact]
    public async Task AbsorbedHeadingsLineUpWithTheirLevelValues()
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        var level = new GroundComfortLevel(RoughnessClass.Rough, "Rough", "1,2 km", 1,
            new("655 mm/s", "as usual"), new("736 mm/s", "−12%"), new("2,7 g"), "+8%");
        var view = new GroundComfortView { DataContext = new GroundComfortReport([level], null, null) };
        await using var mounted = await ViewTestHelpers.MountAsync(view);

        var table = view.FindControl<Grid>("Absorbed")!;
        var headings = (Grid)table.Children[0];
        var row = table.Children[1].GetVisualDescendants().OfType<Grid>().First();

        Assert.Equal(headings.ColumnDefinitions.Count, row.ColumnDefinitions.Count);
        for (var column = 0; column < row.ColumnDefinitions.Count; column++)
            Assert.Equal(Left(headings, column, view), Left(row, column, view), 1);
    }

    private static double Left(Grid grid, int column, Visual root) =>
        grid.Children.Single(child => Grid.GetColumn(child) == column).TranslatePoint(default, root)!.Value.X;
}

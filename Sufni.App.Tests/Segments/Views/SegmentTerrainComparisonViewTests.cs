using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Segments.Views;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Segments.Views;

[Collection("Ui")]
public class SegmentTerrainComparisonViewTests
{
    [AvaloniaFact]
    public async Task SamePositionColumnGivesEachSideItsOwnRepeatRangeStyle()
    {
        var within = new ComparisonRatio(ResponseCellState.WithinRepeatRange, 1.03);
        var outside = new ComparisonRatio(ResponseCellState.Finding, 1.48);
        TerrainComparisonRow row = new(null, 550, new(0.81, 0.93, 1.03),
            Enum.GetValues<ResponseTarget>().ToImmutableDictionary(t => t, _ => within), within, outside);
        await using var vm = await TestSegmentComparisons.CreateWithMatchAsync(new SegmentTerrainComparison.Compared([row], 4));
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        var view = new SegmentTerrainComparisonView { DataContext = vm };
        await using var mounted = await ViewTestHelpers.MountAsync(view);
        Assert.False(view.IsVisible);
        var compare = Assert.Single(vm.TravelRows[0].HeaderActions.Where(a => a.Command is IAsyncRelayCommand));
        await ((IAsyncRelayCommand)compare.Command!).ExecuteAsync(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        var column = Assert.Single(view.GetVisualDescendants().OfType<ItemsControl>()
            .Where(c => c.DataContext is SegmentTerrainColumn { Values.Length: 2 }));
        var cells = column.GetVisualDescendants().OfType<TextBlock>().Where(t => t.DataContext is SegmentTerrainValue).ToArray();
        Assert.Equal(2, cells.Length);
        Assert.True(cells[0].Opacity < cells[1].Opacity);
        Assert.NotNull(ToolTip.GetTip(cells[0]));
        Assert.Null(ToolTip.GetTip(cells[1]));
    }
}

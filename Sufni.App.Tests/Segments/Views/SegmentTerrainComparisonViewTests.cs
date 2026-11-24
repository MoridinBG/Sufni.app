using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Segments.Views;
using Sufni.App.Terrain.Views;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
using static Sufni.App.Tests.TestSupport.Fixtures.TestResponseRows;

namespace Sufni.App.Tests.Segments.Views;

[Collection("Ui")]
public class SegmentTerrainComparisonViewTests
{
    [AvaloniaFact]
    public async Task ComparedRunsShowThePaceLineAndTheResponseReport()
    {
        await using var vm = await TestSegmentComparisons.CreateWithMatchAsync(new SegmentTerrainComparison.Compared([Compared(null, 600, new(0.9, 1.06, 1.12))]));
        await using var mounted = await CompareAsync(vm);
        var view = mounted.View;

        Assert.True(view.FindControl<TextBlock>("Pace")!.IsVisible);
        Assert.True(view.FindControl<TerrainResponseReportView>("Report")!.IsEffectivelyVisible);
        Assert.False(view.FindControl<TextBlock>("Message")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task UnavailableComparisonShowsOnlyItsReason()
    {
        await using var vm = await TestSegmentComparisons.CreateWithMatchAsync(new SegmentTerrainComparison.Unavailable(SegmentTerrainUnavailable.Unaligned));
        await using var mounted = await CompareAsync(vm);
        var view = mounted.View;

        Assert.True(view.FindControl<TextBlock>("Message")!.IsVisible);
        Assert.False(view.FindControl<TextBlock>("Pace")!.IsVisible);
        Assert.False(view.FindControl<TerrainResponseReportView>("Report")!.IsEffectivelyVisible);
    }

    private static async Task<MountedView<SegmentTerrainComparisonView>> CompareAsync(SegmentComparisonViewModel vm)
    {
        ViewTestHelpers.EnsureFluentTheme();
        ViewTestHelpers.EnsureViewTestResources();
        var view = new SegmentTerrainComparisonView { DataContext = vm };
        var mounted = await ViewTestHelpers.MountAsync(view);
        var compare = Assert.Single(vm.TravelRows[0].HeaderActions.Where(a => a.Command is IAsyncRelayCommand));
        await ((IAsyncRelayCommand)compare.Command!).ExecuteAsync(null);
        await ViewTestHelpers.FlushDispatcherAsync();
        return mounted;
    }
}

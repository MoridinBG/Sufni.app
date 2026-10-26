using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Segments.Views;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Segments.Views;

[Collection("Ui")]
public class SegmentAnalysisComparisonBannerViewTests
{
    [AvaloniaFact]
    public async Task Banner_IsHidden_WithoutAComparison()
    {
        var view = new SegmentAnalysisComparisonBannerView();

        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            Assert.False(view.IsVisible);
        }
        finally
        {
            host.Close();
        }
    }

    [AvaloniaFact]
    public async Task ClearButton_StopsComparing()
    {
        using var comparison = await TestSegmentComparisons.CreateWithMatchAsync();
        comparison.TravelRows[0].HeaderActions.Single(action => action.Id == "compare-analysis").Command!.Execute(null);
        var view = new SegmentAnalysisComparisonBannerView { DataContext = comparison };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            Assert.True(view.IsVisible);

            view.FindControl<Button>("ClearButton")!.Command!.Execute(null);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.False(view.IsVisible);
        }
        finally
        {
            host.Close();
        }
    }
}

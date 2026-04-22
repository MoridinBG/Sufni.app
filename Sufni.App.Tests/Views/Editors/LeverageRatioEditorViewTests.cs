using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors.Bike;
using Sufni.App.Views.Editors;

namespace Sufni.App.Tests.Views.Editors;

public class LeverageRatioEditorViewTests
{
    [AvaloniaFact]
    public async Task LeverageRatioEditorView_ShowsSummaryAndPoints_WhenValuePresent()
    {
        var viewModel = new LeverageRatioEditorViewModel(canEdit: true);
        viewModel.ReplaceState(TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 45)));
        var view = new LeverageRatioEditorView
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            Assert.True(view.FindControl<Grid>("SummaryGrid")!.IsVisible);
            Assert.False(view.FindControl<TextBlock>("EmptyStateTextBlock")!.IsVisible);
            Assert.Equal("3", view.FindControl<TextBlock>("PointCountTextBlock")!.Text);
            Assert.Equal("20", view.FindControl<TextBlock>("MaxShockStrokeTextBlock")!.Text);
            Assert.Equal("45", view.FindControl<TextBlock>("MaxWheelTravelTextBlock")!.Text);

            var pointsControl = view.FindControl<ItemsControl>("PointsItemsControl");
            Assert.NotNull(pointsControl);
            Assert.Equal(3, pointsControl!.ItemsSource!.Cast<object>().Count());

            Assert.True(view.FindControl<Grid>("EditorButtonsGrid")!.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task LeverageRatioEditorView_ShowsEmptyState_WhenNoCurveIsLoaded()
    {
        var view = new LeverageRatioEditorView
        {
            DataContext = new LeverageRatioEditorViewModel(canEdit: false)
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            Assert.False(view.FindControl<Grid>("SummaryGrid")!.IsVisible);
            Assert.True(view.FindControl<TextBlock>("EmptyStateTextBlock")!.IsVisible);
            Assert.False(view.FindControl<ItemsControl>("PointsItemsControl")!.IsVisible);
            Assert.False(view.FindControl<Grid>("EditorButtonsGrid")!.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
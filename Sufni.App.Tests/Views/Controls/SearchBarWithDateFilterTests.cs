using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using System.Linq;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class SearchBarWithDateFilterTests
{
    [AvaloniaFact]
    public async Task SearchBarWithDateFilter_HidesDateFilter_WhenCloseButtonIsPressed()
    {
        var viewModel = new ItemListViewModelBase
        {
            DateFilterVisible = true,
        };
        var control = new SearchBarWithDateFilter
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(control);

        var core = mounted.Control.FindFirstVisual<SearchBarCore>();
        Assert.NotNull(core);

        var searchBox = core!.FindControl<TextBox>("SearchBox");
        var closeButton = core.FindControl<Button>("CloseButton");
        var datePickers = mounted.Control.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(border => border.Name == "DatePickers");

        Assert.NotNull(searchBox);
        Assert.NotNull(closeButton);
        Assert.NotNull(datePickers);
        Assert.True(datePickers!.IsVisible);
        Assert.True(closeButton!.IsVisible);

        closeButton.Command!.Execute(closeButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(viewModel.DateFilterVisible);
        Assert.False(closeButton.IsVisible);
    }

    [AvaloniaFact]
    public async Task SearchBarWithDateFilter_ClearsFromAndToDates()
    {
        var viewModel = new ItemListViewModelBase
        {
            DateFilterVisible = true,
            DateFilterFrom = new DateTime(2024, 1, 1),
            DateFilterTo = new DateTime(2024, 1, 31),
        };
        var control = new SearchBarWithDateFilter
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(control);

        var clearFromButton = mounted.Control.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "ClearFromDateButton");
        var clearToButton = mounted.Control.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == "ClearToDateButton");

        Assert.NotNull(clearFromButton);
        Assert.NotNull(clearToButton);
        Assert.True(clearFromButton!.IsVisible);
        Assert.True(clearToButton!.IsVisible);

        clearFromButton.Command!.Execute(clearFromButton.CommandParameter);
        clearToButton.Command!.Execute(clearToButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Null(viewModel.DateFilterFrom);
        Assert.Null(viewModel.DateFilterTo);
    }
}

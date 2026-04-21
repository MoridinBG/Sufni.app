using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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

        var searchBox = mounted.Control.FindControl<TextBox>("SearchBox");
        var closeButton = mounted.Control.FindControl<Button>("CloseButton");
        var datePickers = mounted.Control.FindControl<Border>("DatePickers");

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

        var clearFromButton = mounted.Control.FindControl<Button>("ClearFromDateButton");
        var clearToButton = mounted.Control.FindControl<Button>("ClearToDateButton");

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
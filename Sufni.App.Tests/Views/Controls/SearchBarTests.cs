using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class SearchBarTests
{
    [AvaloniaFact]
    public async Task SearchBar_HidesCloseButton_WhenSearchTextIsNull_AndNotFocused()
    {
        var control = new SearchBar
        {
            DataContext = new ItemListViewModelBase(),
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(control);

        var closeButton = mounted.Control.FindControl<Button>("CloseButton");

        Assert.NotNull(closeButton);
        Assert.False(closeButton!.IsVisible);
    }

    [AvaloniaFact]
    public async Task SearchBar_BindsSearchText_AndClearsIt()
    {
        var viewModel = new ItemListViewModelBase
        {
            SearchText = "shock",
        };
        var control = new SearchBar
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(control);

        var searchBox = mounted.Control.FindControl<TextBox>("SearchBox");
        var closeButton = mounted.Control.FindControl<Button>("CloseButton");

        Assert.NotNull(searchBox);
        Assert.NotNull(closeButton);
        Assert.Equal("shock", searchBox!.Text);
        Assert.True(closeButton!.IsVisible);

        closeButton.Command!.Execute(closeButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Null(viewModel.SearchText);
        Assert.False(viewModel.DateFilterVisible);
        Assert.False(closeButton.IsVisible);
    }
}
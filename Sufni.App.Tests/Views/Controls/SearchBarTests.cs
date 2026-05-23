using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using System.Linq;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class SearchBarTests
{
    [AvaloniaFact]
    public async Task SearchBar_HidesCloseButton_WhenSearchTextIsNull_AndNotFocused()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var control = new SearchBar
        {
            DataContext = new ItemListViewModelBase(new InlineUiThreadDispatcher()),
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(control);

        var core = mounted.Control.FindFirstVisual<SearchBarCore>();
        Assert.NotNull(core);

        var closeButton = core!.FindControl<Button>("CloseButton");

        Assert.NotNull(closeButton);
        Assert.False(closeButton!.IsVisible);
    }

    [AvaloniaFact]
    public async Task SearchBar_BindsSearchText_AndClearsIt()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var viewModel = new ItemListViewModelBase(new InlineUiThreadDispatcher())
        {
            SearchText = "shock",
        };
        var control = new SearchBar
        {
            DataContext = viewModel,
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(control);

        var core = mounted.Control.FindFirstVisual<SearchBarCore>();
        Assert.NotNull(core);

        var searchBox = core!.FindControl<TextBox>("SearchBox");
        var closeButton = core.FindControl<Button>("CloseButton");

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

    [AvaloniaFact]
    public async Task SearchBarCore_DoesNotRenderSyncActivityIndicator()
    {
        ViewTestHelpers.EnsureViewTestResources();
        var control = new SearchBar
        {
            DataContext = new ItemListViewModelBase(new InlineUiThreadDispatcher()),
        };

        await using var mounted = await ListHostTestSupport.MountInSharedMainPagesHostAsync(control);

        var core = mounted.Control.FindFirstVisual<SearchBarCore>();
        Assert.NotNull(core);

        Assert.Empty(core!.GetVisualDescendants().OfType<ActivityIndicator>());
    }
}

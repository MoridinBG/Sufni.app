using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.Views;

namespace Sufni.App.Tests.Views;

public class MainPagesViewTests
{
    [AvaloniaFact]
    public async Task MainPagesView_OpensMenuPane_WhenViewModelCommandExecutes()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var viewModel = MainPagesViewModelTestFactory.Create();
        var view = new MainPagesView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var splitView = mounted.View.FindControl<SplitView>("MainSplitView");

        Assert.NotNull(splitView);
        Assert.False(splitView!.IsPaneOpen);

        viewModel.OpenMenuPaneCommand.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(viewModel.IsMenuPaneOpen);
        Assert.True(splitView.IsPaneOpen);
    }

    [AvaloniaFact]
    public async Task MainPagesView_BindsPrimaryTabsToExpectedPages_AndUpdatesSelectedTab()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var viewModel = MainPagesViewModelTestFactory.Create();
        var view = new MainPagesView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var tabControl = mounted.View.FindControl<TabControl>("PagesTabControl");
        var sessionsTab = mounted.View.FindControl<TabItem>("SessionTabItem");
        var bikesTab = mounted.View.FindControl<TabItem>("BikesTabItem");

        Assert.NotNull(tabControl);
        Assert.NotNull(sessionsTab);
        Assert.NotNull(bikesTab);
        Assert.Same(viewModel.SessionsPage, sessionsTab!.Content);
        Assert.Same(viewModel.BikesPage, bikesTab!.Content);
        Assert.Same(sessionsTab, tabControl!.SelectedItem);

        tabControl.SelectedIndex = 2;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(2, viewModel.SelectedIndex);
        Assert.Equal(2, tabControl.SelectedIndex);
        Assert.Same(bikesTab, tabControl.SelectedItem);
    }

    private static async Task<MountedMainPagesView> MountAsync(MainPagesView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedMainPagesView(host, view);
    }
}

internal sealed class MountedMainPagesView(Window host, MainPagesView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public MainPagesView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.Views;

namespace Sufni.App.Tests.Views;

public class MainViewTests
{
    [AvaloniaFact]
    public async Task MainView_SwitchesMountedContent_WhenCurrentViewChanges()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var mainPages = MainPagesViewModelTestFactory.Create();
        var viewModel = new MainViewModel(mainPages);
        var view = new MainView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var host = mounted.View.FindControl<ContentControl>("CurrentViewHost");

        Assert.NotNull(host);
        Assert.Same(mainPages, host!.Content);
        Assert.Single(mounted.View.GetVisualDescendants().OfType<MainPagesView>());

        viewModel.OpenView(MainPagesViewModelTestFactory.CreateWelcomeScreen());
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.IsType<WelcomeScreenViewModel>(host.Content);
        Assert.Single(mounted.View.GetVisualDescendants().OfType<WelcomeScreenView>());

        viewModel.OpenPreviousView();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Same(mainPages, host.Content);
        Assert.Single(mounted.View.GetVisualDescendants().OfType<MainPagesView>());
    }

    private static async Task<MountedMainView> MountAsync(MainView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedMainView(host, view);
    }
}

internal sealed class MountedMainView(Window host, MainView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public MainView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
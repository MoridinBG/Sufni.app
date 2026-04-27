using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;

namespace Sufni.App.Tests.Views;

public class MainPagesDesktopViewTests
{
    [AvaloniaFact]
    public async Task MainPagesDesktopView_RendersAllPrimaryTabs_AndReflectsSelectedIndex()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var pairingCoordinator = Substitute.For<IPairingServerCoordinator>();
        pairingCoordinator.StartServerAsync().Returns(Task.CompletedTask);

        var viewModel = MainPagesViewModelTestFactory.Create(
            pairingServerViewModel: new PairingServerViewModel(pairingCoordinator));

        var view = new MainPagesDesktopView
        {
            DataContext = viewModel
        };

        await using var mounted = await MountAsync(view);

        var pagesMenu = mounted.View.FindControl<TabControl>("PagesMenu");
        var sessionsTab = mounted.View.FindControl<TabItem>("SessionTabItem");
        var setupsTab = mounted.View.FindControl<TabItem>("BikeSetupsTabItem");
        var bikesTab = mounted.View.FindControl<TabItem>("BikesTabItem");
        var liveTab = mounted.View.FindControl<TabItem>("LiveDaqsTabItem");

        Assert.NotNull(pagesMenu);
        Assert.NotNull(sessionsTab);
        Assert.NotNull(setupsTab);
        Assert.NotNull(bikesTab);
        Assert.NotNull(liveTab);
        Assert.True(sessionsTab!.IsVisible);
        Assert.True(setupsTab!.IsVisible);
        Assert.True(bikesTab!.IsVisible);
        Assert.True(liveTab!.IsVisible);

        pagesMenu!.SelectedIndex = 3;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(3, viewModel.SelectedIndex);
        Assert.Equal(3, pagesMenu.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task MainPagesDesktopView_ShowsPairingRequestPanel_WhenPairingPinExists()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var pairingCoordinator = Substitute.For<IPairingServerCoordinator>();
        pairingCoordinator.StartServerAsync().Returns(Task.CompletedTask);

        var pairingViewModel = new PairingServerViewModel(pairingCoordinator)
        {
            PairingPin = "123456",
            RequestingDisplayName = "Phone",
            Remaining = 0.5,
        };

        var view = new MainPagesDesktopView
        {
            DataContext = MainPagesViewModelTestFactory.Create(pairingServerViewModel: pairingViewModel)
        };

        await using var mounted = await MountAsync(view);

        var pairingPanel = mounted.View.FindControl<Grid>("PairingRequestPanel");

        Assert.NotNull(pairingPanel);
        Assert.True(pairingPanel!.IsVisible);
    }

    private static async Task<MountedMainPagesDesktopView> MountAsync(MainPagesDesktopView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedMainPagesDesktopView(host, view);
    }
}

internal sealed class MountedMainPagesDesktopView(Window host, MainPagesDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public MainPagesDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
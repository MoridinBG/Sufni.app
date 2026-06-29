using System;
using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Svg.Skia;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.Views;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views;

[Collection("Ui")]
public class MainPagesViewTests
{
    [AvaloniaFact]
    public async Task MainPagesView_OpensDrawer_WhenViewModelCommandExecutes()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var viewModel = MainPagesViewModelTestFactory.Create();
        var view = new MainPagesView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var drawerPage = mounted.View.FindControl<DrawerPage>("MainDrawerPage");

        Assert.NotNull(drawerPage);
        Assert.False(drawerPage!.IsOpen);

        viewModel.OpenDrawerCommand.Execute(null);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(viewModel.IsDrawerOpen);
        Assert.True(drawerPage.IsOpen);
    }

    [AvaloniaFact]
    public async Task MainPagesView_TabbedPage_UpdatesSelectedPrimaryIndex_OnSelectionChange()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var viewModel = MainPagesViewModelTestFactory.Create();
        var view = new MainPagesView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var tabbedPage = mounted.View.FindControl<TabbedPage>("PagesTabbedPage");

        Assert.NotNull(tabbedPage);
        Assert.Equal(0, tabbedPage!.SelectedIndex);

        // The bottom tab strip drives the view model's primary-page selection.
        tabbedPage.SelectedIndex = 2;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(2, viewModel.SelectedPrimaryIndex);

        tabbedPage.SelectedIndex = 3;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(3, viewModel.SelectedPrimaryIndex);
    }

    [AvaloniaFact]
    public async Task MainPagesView_TabbedPage_BindsPrimaryPageContent_WithoutSafeAreaPadding()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var viewModel = MainPagesViewModelTestFactory.Create();
        var view = new MainPagesView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var tabbedPage = mounted.View.FindControl<TabbedPage>("PagesTabbedPage")
            ?? throw new InvalidOperationException("Primary tabbed page was not found.");
        var pages = tabbedPage.Pages
            ?? throw new InvalidOperationException("Primary pages were not found.");

        // The primary pages are declared statically and bind their content back to the
        // root view model; the first (selected) page is the representative case.
        var sessionsPage = pages.Cast<ContentPage>().First();

        Assert.False(sessionsPage.AutomaticallyApplySafeAreaPadding);
        Assert.Equal("Sessions", sessionsPage.Header);
        Assert.Same(viewModel.SessionsPage, sessionsPage.Content);
    }

    [AvaloniaFact]
    public async Task MainPagesView_SidePanelBindsGpxImportCommand()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var viewModel = MainPagesViewModelTestFactory.Create();
        var view = new MainPagesView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var menuPanel = mounted.View.FindControl<SidePanel>("MenuPanel");
        var importGpxMenuItem = menuPanel?.FindControl<MenuItem>("ImportGpxMenuItem");

        Assert.NotNull(menuPanel);
        Assert.NotNull(importGpxMenuItem);
        Assert.Same(viewModel.OpenGpsTracksCommand, importGpxMenuItem!.Command);
    }

    [AvaloniaFact]
    public async Task MainPagesView_SidePanelBindsExtensionToolbarActions()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var contribution = new AppToolbarViewContribution(
            "extension",
            "toolbar-action",
            Order: 0,
            new TestContributionViewModel());
        var viewModel = MainPagesViewModelTestFactory.Create(
            appToolbarContributionProviders: [new TestAppToolbarContributionProvider(contribution)]);
        var view = new MainPagesView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var menuPanel = mounted.View.FindControl<SidePanel>("MenuPanel");
        var host = menuPanel!.FindControl<AppToolbarContributionsView>("SidePanelExtensionToolbarActions");
        Assert.NotNull(host);
        Assert.Same(viewModel.ExtensionToolbarCommands, host!.CommandContributions);
        Assert.Same(viewModel.ExtensionToolbarViews, host.ViewContributions);
    }

    [AvaloniaFact]
    public async Task MainPagesView_SidePanelRendersExtensionCommandAsMenuItem()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var command = Substitute.For<ICommand>();
        var commandContribution = new AppToolbarCommandContribution(
            "extension",
            "toolbar-command",
            Order: 0,
            "Mobile command",
            new ToolbarIconDescriptor("/Assets/fa-link.svg", Width: 13, Height: 13),
            command);
        var viewModel = MainPagesViewModelTestFactory.Create(
            appToolbarContributionProviders: [new TestAppToolbarContributionProvider([commandContribution], [])]);
        var view = new MainPagesView
        {
            DataContext = viewModel,
        };

        await using var mounted = await MountAsync(view);

        var menuPanel = mounted.View.FindControl<SidePanel>("MenuPanel");
        var host = menuPanel!.FindControl<AppToolbarContributionsView>("SidePanelExtensionToolbarActions");
        var contributionsHost = host!.FindControl<StackPanel>("ContributionsHost");
        Assert.NotNull(contributionsHost);

        // Command contributions render as menu items matching the built-in side-panel actions.
        var menuItem = Assert.Single(contributionsHost!.Children.OfType<MenuItem>());
        Assert.Equal("Mobile command", menuItem.Header);
        Assert.Same(command, menuItem.Command);
        var icon = Assert.IsType<Image>(menuItem.Icon);
        Assert.Equal(13, icon.Width);
        Assert.IsType<SvgImage>(icon.Source);
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

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
    public async Task MainPagesView_BindsPrimaryPagesToTabbedPage_AndUpdatesSelectedIndex()
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
        Assert.Same(viewModel.PrimaryPages, tabbedPage!.ItemsSource);
        Assert.NotNull(tabbedPage.PageTemplate);
        Assert.Equal(0, tabbedPage.SelectedIndex);

        tabbedPage.SelectedIndex = 2;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(2, viewModel.SelectedPrimaryIndex);
        Assert.Equal(2, tabbedPage.SelectedIndex);

        tabbedPage.SelectedIndex = 3;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(3, viewModel.SelectedPrimaryIndex);
    }

    [AvaloniaFact]
    public async Task MainPagesView_PageTemplate_CreatesSafeAreaFreePrimaryContentPage()
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
        var pageTemplate = tabbedPage.PageTemplate
            ?? throw new InvalidOperationException("Primary page template was not found.");
        var descriptor = viewModel.PrimaryPages[0];

        var contentPage = Assert.IsType<ContentPage>(pageTemplate.Build(descriptor));
        contentPage.DataContext = descriptor;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(contentPage.AutomaticallyApplySafeAreaPadding);
        Assert.Equal(descriptor.Header, contentPage.Header);
        Assert.Same(descriptor.Content, contentPage.Content);
        var icon = Assert.IsType<Image>(contentPage.Icon);
        Assert.Equal(18, icon.Width);
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

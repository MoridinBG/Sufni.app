using Sufni.App.Shell.ViewModels.Base;
using Microsoft.Extensions.Time.Testing;
using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shell.DesktopViews;

[Collection("Ui")]
public class WorkspaceShellViewTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    [AvaloniaFact]
    public async Task MiddleClick_PromptsOnlyOnceAfterRecreatingTheWorkspaceView()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);
        ViewTestHelpers.EnsureFluentTheme();
        await using (var previous = await MountAsync(new WorkspaceShellView { DataContext = CreateRoot() })) { }
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowCloseConfirmationAsync(Arg.Any<bool>()).Returns(PromptResult.Cancel);
        var tab = new TestTabPageViewModel(dialogs) { Name = "Unsaved", IsDirty = true };
        var root = CreateRoot();
        root.Workspace.OpenOrFocus(tab);
        await using var mounted = await MountAsync(new WorkspaceShellView { DataContext = root });
        var item = Assert.Single(mounted.View.GetVisualDescendants().OfType<TabStripItem>());
        var point = item.TranslatePoint(new Point(item.Bounds.Width / 2, item.Bounds.Height / 2), mounted.Host)!.Value;

        mounted.Host.MouseDown(point, MouseButton.Middle, RawInputModifiers.None);
        mounted.Host.MouseUp(point, MouseButton.Middle, RawInputModifiers.None);
        await ViewTestHelpers.FlushDispatcherAsync();

        await dialogs.Received(1).ShowCloseConfirmationAsync(Arg.Any<bool>());
        Assert.Same(tab, root.Workspace.CurrentTab);
    }

    [AvaloniaFact]
    public async Task WorkspaceShellView_BindsPagesAndShowsEmptyWorkspaceState_WhenNoTabIsSelected()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var root = CreateRoot();
        var view = new WorkspaceShellView
        {
            DataContext = root,
        };

        await using var mounted = await MountAsync(view);

        var pagesHost = mounted.View.FindControl<ContentControl>("PagesHost");
        var emptyState = mounted.View.FindControl<Border>("EmptyWorkspaceState");
        var tabControl = mounted.View.FindControl<TabStrip>("TabControl");

        Assert.True(emptyState!.IsVisible);
        var tab = new TestTabPageViewModel { Name = "Session" };
        root.Workspace.OpenOrFocus(tab);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(emptyState.IsVisible);
        Assert.Same(tab, tabControl!.SelectedItem);
        root.Workspace.CloseTab(tab, rememberForRestore: false);
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(emptyState.IsVisible);
    }

    private static ShellRootViewModel CreateRoot()
    {
        var environment = new AppEnvironment(
            LayoutProfile: UiLayoutProfile.Workspace,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: true,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true,
                CanPickFolder: true,
                CanPlayVideo: false),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsLongPressContextMenu: false,
                CommandModifier: CommandModifier.Control));

        return ShellRootViewModelTestFactory.Create(
            MainPagesViewModelTestFactory.Create(),
            new ShellWorkspaceViewModel(TestDispatcher, timeProvider: new FakeTimeProvider()),
            environment,
            Substitute.For<IPlotZoomState>(),
            TestDispatcher,
            timeProvider: new FakeTimeProvider());
    }

    private static async Task<MountedView<WorkspaceShellView>> MountAsync(WorkspaceShellView view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedView<WorkspaceShellView>(host, view);
    }
}

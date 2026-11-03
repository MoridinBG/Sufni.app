using Microsoft.Extensions.Time.Testing;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.Shell.KeyboardShortcuts;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Tests.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shell.DesktopViews;

// Mounted windows require Avalonia resources.
[Collection("Ui")]
public class MainWindowTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    [AvaloniaFact]
    public async Task MainWindow_HandlesRegisteredTabShortcutsBeforeFocusNavigation()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var root = CreateRoot();
        var first = new TestTabPageViewModel();
        var second = new TestTabPageViewModel();
        var third = new TestTabPageViewModel();
        root.Workspace.OpenOrFocus(first);
        root.Workspace.OpenOrFocus(second);
        root.Workspace.OpenOrFocus(third);

        await using var mounted = await MountAsync(new MainWindow
        {
            DataContext = root,
            Width = 900,
            Height = 700,
        });

        var next = Shortcut(
            KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow,
            KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab);
        var handledNext = RaiseKey(mounted.View, next.Key, next.KeyModifiers);

        Assert.True(handledNext);
        Assert.Same(first, root.Workspace.CurrentTab);

        var previous = Shortcut(
            KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow,
            KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab);
        var handledPrevious = RaiseKey(mounted.View, previous.Key, previous.KeyModifiers);

        Assert.True(handledPrevious);
        Assert.Same(third, root.Workspace.CurrentTab);
    }

    [AvaloniaFact]
    public async Task MainWindow_HandlesRegisteredTabShortcutFromHandledKeyDownEvent()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var root = CreateRoot();
        var first = new TestTabPageViewModel();
        var second = new TestTabPageViewModel();
        root.Workspace.OpenOrFocus(first);
        root.Workspace.OpenOrFocus(second);

        await using var mounted = await MountAsync(new MainWindow
        {
            DataContext = root,
            Width = 900,
            Height = 700,
        });

        var next = Shortcut(
            KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow,
            KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab);
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = mounted.View,
            Key = next.Key,
            KeyModifiers = next.KeyModifiers,
            Handled = true,
        };

        mounted.View.RaiseEvent(args);

        Assert.True(args.Handled);
        Assert.Same(first, root.Workspace.CurrentTab);
    }

    [AvaloniaFact]
    public async Task MainWindow_PlatformAttachmentHandlesTabIntent_AndEndsOnClose()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);
        var root = CreateRoot();
        var first = new TestTabPageViewModel();
        root.Workspace.OpenOrFocus(first);
        root.Workspace.OpenOrFocus(new TestTabPageViewModel());
        var behavior = Substitute.For<IWindowBehavior>();
        var attachment = Substitute.For<IDisposable>();
        Func<Key, KeyModifiers, bool>? handle = null;
        behavior.Attach(Arg.Any<Avalonia.Controls.Window>(), Arg.Any<Func<Key, KeyModifiers, bool>>())
            .Returns(call =>
            {
                handle = call.Arg<Func<Key, KeyModifiers, bool>>();
                return attachment;
            });

        await using var mounted = await MountAsync(new MainWindow(behavior)
        {
            DataContext = root,
            Width = 900,
            Height = 700,
        });

        Assert.True(handle!(Key.Tab, KeyModifiers.Control));
        Assert.Same(first, root.Workspace.CurrentTab);
        attachment.DidNotReceive().Dispose();
        mounted.View.Close();
        attachment.Received(1).Dispose();
    }

    private static async Task<MountedView<MainWindow>> MountAsync(MainWindow window)
    {
        ViewTestHelpers.ShowWindow(window);
        await ViewTestHelpers.FlushDispatcherAsync();

        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        await ViewTestHelpers.FlushDispatcherAsync();

        return new MountedView<MainWindow>(window, window);
    }

    private static bool RaiseKey(MainWindow window, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Source = window,
            Key = key,
            KeyModifiers = modifiers,
        };
        window.RaiseEvent(args);
        return args.Handled;
    }

    private static KeyGesture Shortcut(string source, string id, int index = 0) =>
        ((KeyboardShortcutRegistry)Application.Current!.Resources[typeof(KeyboardShortcutRegistry)]!).GesturesBySource[source][id][index];

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
}

using Microsoft.Extensions.Time.Testing;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.macOS;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.Shell.KeyboardShortcuts;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Tests.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shell.DesktopViews;

// Mounted windows and automation peers require Avalonia resources.
[Collection("Ui")]
public class MainWindowTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    [AvaloniaTheory]
    [InlineData(CommandModifier.Control, KeyModifiers.Control)]
    [InlineData(CommandModifier.Meta, KeyModifiers.Meta)]
    public async Task MainWindow_BindsNavigationShortcutFromInputCapability(CommandModifier modifier, KeyModifiers expected)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);
        var root = CreateRoot();
        var first = new TestTabPageViewModel();
        root.Workspace.OpenOrFocus(first);
        root.Workspace.OpenOrFocus(new TestTabPageViewModel());
        var input = TestAppEnvironment.Create(UiLayoutProfile.Workspace).Input with { CommandModifier = modifier };
        Application.Current!.Resources[typeof(KeyboardShortcutRegistry)] = new KeyboardShortcutRegistry(input);

        await using var mounted = await MountAsync(new MainWindow
        {
            DataContext = root,
            Width = 900,
            Height = 700,
        });

        var binding = Assert.Single(mounted.Window.KeyBindings, binding =>
            binding.Gesture is { Key: Key.OemCloseBrackets } gesture &&
            gesture.KeyModifiers == (expected | KeyModifiers.Shift));
        binding.Command!.Execute(binding.CommandParameter);

        Assert.Same(first, root.Workspace.CurrentTab);
    }

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
        var handledNext = RaiseKey(mounted.Window, next.Key, next.KeyModifiers);

        Assert.True(handledNext);
        Assert.Same(first, root.Workspace.CurrentTab);

        var previous = Shortcut(
            KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow,
            KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab);
        var handledPrevious = RaiseKey(mounted.Window, previous.Key, previous.KeyModifiers);

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
            Source = mounted.Window,
            Key = next.Key,
            KeyModifiers = next.KeyModifiers,
            Handled = true,
        };

        mounted.Window.RaiseEvent(args);

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
        mounted.Window.Close();
        attachment.Received(1).Dispose();
    }

    [AvaloniaFact]
    public async Task MainWindow_DoesNotInterceptRegisteredNonTabShortcuts()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var root = CreateRoot();
        var first = new TestTabPageViewModel();
        root.Workspace.OpenOrFocus(first);

        await using var mounted = await MountAsync(new MainWindow
        {
            DataContext = root,
            Width = 900,
            Height = 700,
        });

        var bracketShortcut = Shortcut(
            KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow,
            KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab,
            index: 1);

        Assert.False(RaiseKey(mounted.Window,
            bracketShortcut.Key,
            bracketShortcut.KeyModifiers));
        Assert.Same(first, root.Workspace.CurrentTab);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MainWindow_AutomationPeer_FollowsPlatformBehavior(bool useMacBehavior)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Workspace);

        var behavior = Substitute.For<IWindowBehavior>();
        behavior.CreateAutomationPeer(Arg.Any<Avalonia.Controls.Window>())
            .Returns(call => new InertWindowAutomationPeer(call.Arg<Avalonia.Controls.Window>()));
        await using var mounted = await MountAsync(new MainWindow(useMacBehavior ? behavior : null)
        {
            DataContext = CreateRoot(),
            Width = 900,
            Height = 700,
        });

        var peer = ControlAutomationPeer.CreatePeerForElement(mounted.Window);

        if (useMacBehavior)
        {
            Assert.Empty(peer.GetChildren());
            Assert.Null(peer.GetProvider<IRootProvider>());
        }
        else
        {
            Assert.NotNull(peer.GetProvider<IRootProvider>());
        }
    }

    private static async Task<MountedMainWindow> MountAsync(MainWindow window)
    {
        ViewTestHelpers.ShowWindow(window);
        await ViewTestHelpers.FlushDispatcherAsync();

        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        await ViewTestHelpers.FlushDispatcherAsync();

        return new MountedMainWindow(window);
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
                CanPlayVideo: false),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsLongPressContextMenu: false,
                CommandModifier: CommandModifier.Control));

        return new ShellRootViewModel(
            MainPagesViewModelTestFactory.Create(),
            new ShellWorkspaceViewModel(TestDispatcher, timeProvider: new FakeTimeProvider()),
            environment,
            Substitute.For<IPlotZoomState>(),
            TestDispatcher,
            timeProvider: new FakeTimeProvider());
    }
}

internal sealed class MountedMainWindow(MainWindow window) : IAsyncDisposable
{
    public MainWindow Window { get; } = window;

    public async ValueTask DisposeAsync()
    {
        Window.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}

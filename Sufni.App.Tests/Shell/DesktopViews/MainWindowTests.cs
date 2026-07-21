using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.Shell.KeyboardShortcuts;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Tests.Shell.ViewModels;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shell.DesktopViews;

[Collection("Ui")]
public class MainWindowTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    [AvaloniaFact]
    public async Task MainWindow_UsesShortcutRegistryForWindowShortcuts()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        await using var mounted = await MountAsync(new MainWindow
        {
            Width = 900,
            Height = 700,
        });

        Assert.Contains(
            mounted.Window.KeyBindings,
            binding => HasGesture(
                binding,
                Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.CloseCurrentTab)));
        Assert.Contains(
            mounted.Window.KeyBindings,
            binding => HasGesture(
                binding,
                Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.RestoreClosedTab)));
        Assert.Contains(
            mounted.Window.KeyBindings,
            binding => HasGesture(
                binding,
                Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab)));
        Assert.Contains(
            mounted.Window.KeyBindings,
            binding => HasGesture(
                binding,
                Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab, 1)));
        Assert.Contains(
            mounted.Window.KeyBindings,
            binding => HasGesture(
                binding,
                Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab)));
        Assert.Contains(
            mounted.Window.KeyBindings,
            binding => HasGesture(
                binding,
                Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab, 1)));
    }

    [AvaloniaFact]
    public async Task MainWindow_HandlesRegisteredTabShortcutsBeforeFocusNavigation()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

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
        var handledNext = mounted.Window.TryHandleRegisteredTabShortcut(next.Key, next.KeyModifiers);

        Assert.True(handledNext);
        Assert.Same(first, root.Workspace.CurrentTab);

        var previous = Shortcut(
            KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow,
            KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab);
        var handledPrevious = mounted.Window.TryHandleRegisteredTabShortcut(previous.Key, previous.KeyModifiers);

        Assert.True(handledPrevious);
        Assert.Same(third, root.Workspace.CurrentTab);
    }

    [AvaloniaFact]
    public async Task MainWindow_HandlesRegisteredTabShortcutFromHandledKeyDownEvent()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

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
    public async Task MainWindow_HandlesRegisteredTabShortcutFromRawKeyUp_WhenRawKeyDownIsMissing()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

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

        var handled = mounted.Window.TryHandleRawTabShortcut(
            RawKeyEventType.KeyUp,
            Key.Tab,
            KeyModifiers.Control);

        Assert.True(handled);
        Assert.Same(first, root.Workspace.CurrentTab);
    }

    [AvaloniaFact]
    public async Task MainWindow_SuppressesRawTabKeyUp_WhenRawKeyDownAlreadyHandledShortcut()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

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

        var handledDown = mounted.Window.TryHandleRawTabShortcut(
            RawKeyEventType.KeyDown,
            Key.Tab,
            KeyModifiers.Control);
        var selectedAfterDown = root.Workspace.CurrentTab;
        var handledUp = mounted.Window.TryHandleRawTabShortcut(
            RawKeyEventType.KeyUp,
            Key.Tab,
            KeyModifiers.Control);

        Assert.True(handledDown);
        Assert.True(handledUp);
        Assert.Same(first, selectedAfterDown);
        Assert.Same(first, root.Workspace.CurrentTab);
    }

    [AvaloniaFact]
    public async Task MainWindow_DoesNotInterceptRegisteredNonTabShortcuts()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

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

        Assert.False(mounted.Window.TryHandleRegisteredTabShortcut(
            bracketShortcut.Key,
            bracketShortcut.KeyModifiers));
        Assert.Same(first, root.Workspace.CurrentTab);
    }

    [AvaloniaFact]
    public async Task MainWindow_AutomationPeer_BlocksDescentFocusAndHitTest_OnMacOS()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        await using var mounted = await MountAsync(new MainWindow
        {
            DataContext = CreateRoot(),
            Width = 900,
            Height = 700,
        });

        var peer = ControlAutomationPeer.CreatePeerForElement(mounted.Window);

        if (OperatingSystem.IsMacOS())
        {
            Assert.IsType<InertWindowAutomationPeer>(peer);
            Assert.Empty(peer.GetChildren());
            Assert.Null(peer.GetProvider<IRootProvider>());
        }
        else
        {
            Assert.IsNotType<InertWindowAutomationPeer>(peer);
            Assert.NotNull(peer.GetProvider<IRootProvider>());
        }
    }

    private static async Task<MountedMainWindow> MountAsync(MainWindow window)
    {
        window.Show();
        await ViewTestHelpers.FlushDispatcherAsync();

        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
        await ViewTestHelpers.FlushDispatcherAsync();

        return new MountedMainWindow(window);
    }

    private static bool HasGesture(KeyBinding binding, KeyGesture expected) =>
        binding.Gesture is { } actual &&
        actual.Key == expected.Key &&
        actual.KeyModifiers == expected.KeyModifiers;

    private static KeyGesture Shortcut(string source, string id, int index = 0) =>
        KeyboardShortcutRegistry.GesturesBySource[source][id][index];

    private static ShellRootViewModel CreateRoot()
    {
        var environment = new AppEnvironment(
            DefaultLayoutProfile: UiLayoutProfile.Workspace,
            LayoutProfile: UiLayoutProfile.Workspace,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: true,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsLongPressContextMenu: false));

        return new ShellRootViewModel(
            MainPagesViewModelTestFactory.Create(),
            new ShellWorkspaceViewModel(TestDispatcher),
            environment,
            Substitute.For<IPlotZoomState>(),
            TestDispatcher);
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

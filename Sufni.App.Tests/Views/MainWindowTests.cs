using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Sufni.App.DesktopViews;
using Sufni.App.KeyboardShortcuts;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;

namespace Sufni.App.Tests.Views;

public class MainWindowTests
{
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

        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var viewModel = new MainWindowViewModel(
            MainPagesViewModelTestFactory.Create(),
            welcome,
            new InlineUiThreadDispatcher());
        var first = new TestTabPageViewModel();
        var second = new TestTabPageViewModel();
        viewModel.OpenView(first);
        viewModel.OpenView(second);
        viewModel.OpenView(welcome);

        await using var mounted = await MountAsync(new MainWindow
        {
            DataContext = viewModel,
            Width = 900,
            Height = 700,
        });

        var next = Shortcut(
            KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow,
            KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab);
        var handledNext = mounted.Window.TryHandleRegisteredTabShortcut(next.Key, next.KeyModifiers);

        Assert.True(handledNext);
        Assert.Same(first, viewModel.CurrentView);

        var previous = Shortcut(
            KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow,
            KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab);
        var handledPrevious = mounted.Window.TryHandleRegisteredTabShortcut(previous.Key, previous.KeyModifiers);

        Assert.True(handledPrevious);
        Assert.Same(welcome, viewModel.CurrentView);
    }

    [AvaloniaFact]
    public async Task MainWindow_HandlesRegisteredTabShortcutFromHandledKeyDownEvent()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var viewModel = new MainWindowViewModel(
            MainPagesViewModelTestFactory.Create(),
            welcome,
            new InlineUiThreadDispatcher());
        var first = new TestTabPageViewModel();
        viewModel.OpenView(first);
        viewModel.OpenView(welcome);

        await using var mounted = await MountAsync(new MainWindow
        {
            DataContext = viewModel,
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
        Assert.Same(first, viewModel.CurrentView);
    }

    [AvaloniaFact]
    public async Task MainWindow_HandlesRegisteredTabShortcutFromRawKeyUp_WhenRawKeyDownIsMissing()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var viewModel = new MainWindowViewModel(
            MainPagesViewModelTestFactory.Create(),
            welcome,
            new InlineUiThreadDispatcher());
        var first = new TestTabPageViewModel();
        viewModel.OpenView(first);
        viewModel.OpenView(welcome);

        await using var mounted = await MountAsync(new MainWindow
        {
            DataContext = viewModel,
            Width = 900,
            Height = 700,
        });

        var handled = mounted.Window.TryHandleRawTabShortcut(
            RawKeyEventType.KeyUp,
            Key.Tab,
            KeyModifiers.Control);

        Assert.True(handled);
        Assert.Same(first, viewModel.CurrentView);
    }

    [AvaloniaFact]
    public async Task MainWindow_SuppressesRawTabKeyUp_WhenRawKeyDownAlreadyHandledShortcut()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var viewModel = new MainWindowViewModel(
            MainPagesViewModelTestFactory.Create(),
            welcome,
            new InlineUiThreadDispatcher());
        var first = new TestTabPageViewModel();
        viewModel.OpenView(first);
        viewModel.OpenView(welcome);

        await using var mounted = await MountAsync(new MainWindow
        {
            DataContext = viewModel,
            Width = 900,
            Height = 700,
        });

        var handledDown = mounted.Window.TryHandleRawTabShortcut(
            RawKeyEventType.KeyDown,
            Key.Tab,
            KeyModifiers.Control);
        var selectedAfterDown = viewModel.CurrentView;
        var handledUp = mounted.Window.TryHandleRawTabShortcut(
            RawKeyEventType.KeyUp,
            Key.Tab,
            KeyModifiers.Control);

        Assert.True(handledDown);
        Assert.True(handledUp);
        Assert.Same(first, selectedAfterDown);
        Assert.Same(first, viewModel.CurrentView);
    }

    [AvaloniaFact]
    public async Task MainWindow_DoesNotInterceptRegisteredNonTabShortcuts()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var viewModel = new MainWindowViewModel(
            MainPagesViewModelTestFactory.Create(),
            welcome,
            new InlineUiThreadDispatcher());
        var first = new TestTabPageViewModel();
        viewModel.OpenView(first);

        await using var mounted = await MountAsync(new MainWindow
        {
            DataContext = viewModel,
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
        Assert.Same(first, viewModel.CurrentView);
    }

    [AvaloniaFact]
    public async Task MainWindow_TabDragFeedback_FadesDraggedTabAndShowsInsertionIndicator()
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: true);

        await using var mounted = await MountAsync(new MainWindow
        {
            Width = 900,
            Height = 700,
        });

        var tabItem = new TabStripItem();

        mounted.Window.BeginTabDragFeedback(tabItem);
        mounted.Window.ShowTabDropIndicator(120);

        Assert.True(mounted.Window.IsTabDragFeedbackVisible);
        Assert.True(tabItem.Opacity < 1);
        Assert.True(mounted.Window.IsTabDropIndicatorVisible);
        Assert.True(mounted.Window.TabDropIndicatorX > 0);

        mounted.Window.EndTabDragFeedback();

        Assert.False(mounted.Window.IsTabDragFeedbackVisible);
        Assert.Equal(1, tabItem.Opacity);
        Assert.False(mounted.Window.IsTabDropIndicatorVisible);
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

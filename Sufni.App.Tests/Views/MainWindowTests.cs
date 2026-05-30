using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Sufni.App.DesktopViews;
using Sufni.App.KeyboardShortcuts;
using Sufni.App.Tests.Infrastructure;

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

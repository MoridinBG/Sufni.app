using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;

using Sufni.App.Shell.KeyboardShortcuts;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Shell.DesktopViews;

public partial class MainWindow : Window
{
    private RawTabShortcutHandler? rawTabShortcutHandler;

    public MainWindow()
    {
        InitializeComponent();
        rawTabShortcutHandler = RawTabShortcutHandler.Attach(TryHandleRegisteredTabShortcut);
        Closed += (_, _) => DisposeRawTabShortcutHandling();

        AddHandler<KeyEventArgs>(
            InputElement.KeyDownEvent,
            OnWindowKeyDown,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void DisposeRawTabShortcutHandling()
    {
        rawTabShortcutHandler?.Dispose();
        rawTabShortcutHandler = null;
    }

    internal bool TryHandleRawTabShortcut(RawKeyEventType type, Key key, KeyModifiers modifiers) =>
        rawTabShortcutHandler?.TryHandleRawTabShortcut(type, key, modifiers) == true;

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (TryHandleRegisteredTabShortcut(args.Key, args.KeyModifiers))
        {
            args.Handled = true;
        }
    }

    internal bool TryHandleRegisteredTabShortcut(Key key, KeyModifiers modifiers)
    {
        if (DataContext is not ShellRootViewModel root)
        {
            return false;
        }

        if (MatchesMainWindowTabShortcut(key, modifiers, KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab))
        {
            root.Workspace.SelectNextTabCommand.Execute(null);
            return true;
        }

        if (MatchesMainWindowTabShortcut(key, modifiers, KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab))
        {
            root.Workspace.SelectPreviousTabCommand.Execute(null);
            return true;
        }

        return false;
    }

    private static bool MatchesMainWindowTabShortcut(Key key, KeyModifiers modifiers, string shortcutId)
    {
        var gestures = KeyboardShortcutRegistry
            .GesturesBySource[KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow][shortcutId];
        return gestures.Any(gesture => gesture.Key == Key.Tab &&
                                       gesture.Key == key &&
                                       gesture.KeyModifiers == modifiers);
    }
}

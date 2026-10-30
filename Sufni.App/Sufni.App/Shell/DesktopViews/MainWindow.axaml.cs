using System;
using System.Linq;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Sufni.App.Shell.KeyboardShortcuts;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Shell.DesktopViews;

public partial class MainWindow : Window
{
    private readonly IWindowBehavior? windowBehavior;

    public MainWindow() : this(null)
    {
    }

    public MainWindow(IWindowBehavior? windowBehavior)
    {
        this.windowBehavior = windowBehavior;
        InitializeComponent();
        var attachment = windowBehavior?.Attach(this, TryHandleRegisteredTabShortcut);
        Closed += (_, _) => attachment?.Dispose();

        AddHandler<KeyEventArgs>(
            InputElement.KeyDownEvent,
            OnWindowKeyDown,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        windowBehavior?.CreateAutomationPeer(this) ?? base.OnCreateAutomationPeer();

    private void OnWindowKeyDown(object? sender, KeyEventArgs args)
    {
        if (TryHandleRegisteredTabShortcut(args.Key, args.KeyModifiers))
        {
            args.Handled = true;
        }
    }

    private bool TryHandleRegisteredTabShortcut(Key key, KeyModifiers modifiers)
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

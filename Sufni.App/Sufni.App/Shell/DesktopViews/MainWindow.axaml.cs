using System;
using System.Linq;
using Avalonia.Automation.Peers;
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

    // WARNING: This intentionally disables the entire Avalonia accessibility tree on macOS.
    // VoiceOver, Accessibility Inspector, and accessibility-driven UI automation cannot inspect
    // any Sufni control while this workaround is active. Remove it as soon as Avalonia.Native
    // provides a safe peer/node detach lifecycle.
    //
    // The macOS accessibility bridge permanently retains every automation peer it
    // materializes (element/node ownership cycle in Avalonia.Native), which keeps
    // closed session content alive. An inert peer keeps AX clients from reaching
    // any descendant peer through tree descent, focus queries, or hit-testing.
    protected override AutomationPeer OnCreateAutomationPeer() =>
        OperatingSystem.IsMacOS()
            ? new InertWindowAutomationPeer(this)
            : base.OnCreateAutomationPeer();

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

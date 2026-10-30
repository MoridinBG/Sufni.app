using System;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Sufni.App.Shell.DesktopViews;

namespace Sufni.App.macOS;

internal sealed class MacOsWindowBehavior : IWindowBehavior
{
    public IDisposable Attach(Window window, Func<Key, KeyModifiers, bool> handleTabShortcut)
    {
        // Avalonia Native can emit Control+Tab key-up without a matching key-down.
        var handler = new RawTabShortcutHandler(handleTabShortcut);
        var inputManager = (IInputManager)AvaloniaLocator.Current.GetService(typeof(IInputManager))!;
        return inputManager.PreProcess.Subscribe(raw =>
        {
            if (raw is RawKeyEventArgs key &&
                handler.Handle(key.Type, key.Key, (KeyModifiers)key.Modifiers))
            {
                key.Handled = true;
            }
        });
    }

    // Disable accessibility until Avalonia.Native can release materialized peers.
    public AutomationPeer CreateAutomationPeer(Window window) => new InertWindowAutomationPeer(window);
}

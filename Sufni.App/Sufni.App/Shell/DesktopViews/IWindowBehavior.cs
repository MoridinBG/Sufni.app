using System;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;

namespace Sufni.App.Shell.DesktopViews;

/// <summary>Native window integration whose attachment lasts until the window closes.</summary>
public interface IWindowBehavior
{
    IDisposable Attach(Window window, Func<Key, KeyModifiers, bool> handleTabShortcut);
    AutomationPeer CreateAutomationPeer(Window window);
}

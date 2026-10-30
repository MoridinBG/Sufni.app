using System;
using Avalonia.Input;
using Avalonia.Input.Raw;

namespace Sufni.App.macOS;

internal sealed class RawTabShortcutHandler(Func<Key, KeyModifiers, bool> handleTabShortcut)
{
    private bool suppressNextRawTabKeyUp;

    public bool Handle(RawKeyEventType type, Key key, KeyModifiers modifiers)
    {
        if (key != Key.Tab)
        {
            return false;
        }

        if (type == RawKeyEventType.KeyUp && suppressNextRawTabKeyUp)
        {
            suppressNextRawTabKeyUp = false;
            return true;
        }

        if (type is not (RawKeyEventType.KeyDown or RawKeyEventType.KeyUp) ||
            !handleTabShortcut(key, modifiers))
        {
            return false;
        }

        suppressNextRawTabKeyUp = type == RawKeyEventType.KeyDown;
        return true;
    }

}

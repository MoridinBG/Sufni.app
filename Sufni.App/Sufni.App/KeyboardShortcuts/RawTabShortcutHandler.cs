using System;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Input;
using Avalonia.Input.Raw;

namespace Sufni.App.KeyboardShortcuts;

internal sealed class RawTabShortcutHandler : IDisposable
{
    // macOS/Avalonia Native can report Control+Tab as Tab key-up without a matching
    // Tab key-down, so routed KeyDown/key bindings never see that shortcut.
    private readonly Func<Key, KeyModifiers, bool> handleTabShortcut;
    private readonly IDisposable? rawInputSubscription;
    private bool suppressNextRawTabKeyUp;

    private RawTabShortcutHandler(
        Func<Key, KeyModifiers, bool> handleTabShortcut,
        object? inputManager)
    {
        this.handleTabShortcut = handleTabShortcut;
        rawInputSubscription = inputManager is null
            ? null
            : SubscribeToRawInputPreProcess(inputManager);
    }

    public static RawTabShortcutHandler Attach(Func<Key, KeyModifiers, bool> handleTabShortcut) =>
        new(handleTabShortcut, ResolveInputManager());

    public void Dispose() =>
        rawInputSubscription?.Dispose();

    internal bool TryHandleRawTabShortcut(RawKeyEventType type, Key key, KeyModifiers modifiers)
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

    private static object? ResolveInputManager()
    {
        var locatorType = typeof(AvaloniaLocator);
        var resolver = locatorType.GetProperty("Current", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null)
            ?? locatorType.GetProperty("CurrentMutable", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
        return resolver
            ?.GetType()
            .GetMethod("GetService", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [typeof(Type)])
            ?.Invoke(resolver, [typeof(IInputManager)]);
    }

    private IDisposable? SubscribeToRawInputPreProcess(object inputManager)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var property = inputManager
            .GetType()
            .GetProperties(flags)
            .FirstOrDefault(candidate =>
                candidate.Name == "PreProcess" ||
                candidate.Name.EndsWith(".PreProcess", StringComparison.Ordinal));
        var observable = property?.GetValue(inputManager);
        return observable is IObservable<RawInputEventArgs> rawObservable
            ? rawObservable.Subscribe(HandleRawInputPreProcess)
            : null;
    }

    private void HandleRawInputPreProcess(RawInputEventArgs raw)
    {
        if (raw is not RawKeyEventArgs keyArgs ||
            !TryReadRawKeyShortcut(keyArgs, out var type, out var key, out var modifiers) ||
            !TryHandleRawTabShortcut(type, key, modifiers))
        {
            return;
        }

        SetRawHandled(keyArgs);
    }

    private static bool TryReadRawKeyShortcut(
        RawKeyEventArgs keyArgs,
        out RawKeyEventType type,
        out Key key,
        out KeyModifiers modifiers)
    {
        type = default;
        key = default;
        modifiers = default;

        var typeValue = GetRawMemberValue(keyArgs, "Type");
        var keyValue = GetRawMemberValue(keyArgs, "Key");
        var modifiersValue = GetRawMemberValue(keyArgs, "Modifiers");

        if (typeValue is not RawKeyEventType rawType ||
            keyValue is not Key rawKey ||
            modifiersValue is not RawInputModifiers rawModifiers)
        {
            return false;
        }

        type = rawType;
        key = rawKey;
        modifiers = (KeyModifiers)rawModifiers;
        return true;
    }

    private static void SetRawHandled(RawInputEventArgs raw)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        raw.GetType()
            .GetProperty("Handled", flags)
            ?.SetValue(raw, true);
    }

    private static object? GetRawMemberValue(object instance, string name)
    {
        var type = instance.GetType();
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property is not null && property.GetIndexParameters().Length == 0)
        {
            return ReadRawProperty(instance, property);
        }

        return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
    }

    private static object? ReadRawProperty(object instance, PropertyInfo property)
    {
        try
        {
            return property.GetValue(instance);
        }
        catch (Exception ex)
        {
            return $"<unreadable:{ex.GetType().Name}>";
        }
    }
}

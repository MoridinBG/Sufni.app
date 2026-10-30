using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Sufni.App.Infrastructure;

namespace Sufni.App.Shell.KeyboardShortcuts;

public sealed class KeyboardShortcutRegistry
{
    public static class ShortcutConfiguration
    {
        public const string MainWindow = "DesktopViews.MainWindow";
        public const string BikeImageDesktopView = "DesktopViews.Items.BikeImageDesktopView";
        public const string BikeImageControlsDesktopView = "DesktopViews.Items.BikeImageControlsDesktopView";

        public const string CloseCurrentTab = "close-current-tab";
        public const string RestoreClosedTab = "restore-closed-tab";
        public const string DeleteSelection = "delete-selection";
        public const string SelectNextTab = "select-next-tab";
        public const string SelectPreviousTab = "select-previous-tab";
    }

    public FrozenDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>>> GesturesBySource { get; }

    public KeyboardShortcutRegistry(InputCapabilities input)
    {
        var commandModifier = input.CommandModifier == CommandModifier.Meta
            ? KeyModifiers.Meta
            : KeyModifiers.Control;
        GesturesBySource =
            new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>>>
            {
                [ShortcutConfiguration.MainWindow] = new Dictionary<string, IReadOnlyList<KeyGesture>>
                {
                    [ShortcutConfiguration.CloseCurrentTab] = [Gesture(Key.W, commandModifier)],
                    [ShortcutConfiguration.RestoreClosedTab] = [Gesture(Key.T, commandModifier | KeyModifiers.Shift)],
                    [ShortcutConfiguration.SelectNextTab] =
                    [
                        Gesture(Key.Tab, KeyModifiers.Control),
                        Gesture(Key.OemCloseBrackets, commandModifier | KeyModifiers.Shift),
                    ],
                    [ShortcutConfiguration.SelectPreviousTab] =
                    [
                        Gesture(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift),
                        Gesture(Key.OemOpenBrackets, commandModifier | KeyModifiers.Shift),
                    ],
                },
                [ShortcutConfiguration.BikeImageDesktopView] = new Dictionary<string, IReadOnlyList<KeyGesture>>
                {
                    [ShortcutConfiguration.DeleteSelection] = [Gesture(Key.Delete, KeyModifiers.None)],
                },
                [ShortcutConfiguration.BikeImageControlsDesktopView] = new Dictionary<string, IReadOnlyList<KeyGesture>>
                {
                    [ShortcutConfiguration.DeleteSelection] = [Gesture(Key.Delete, KeyModifiers.None)],
                },
            }.ToFrozenDictionary();
    }

    public IReadOnlyList<KeyGesture> All =>
        GesturesBySource.Values
            .SelectMany(bindingsById => bindingsById.Values)
            .SelectMany(gestures => gestures)
            .GroupBy(gesture => new { gesture.Key, gesture.KeyModifiers })
            .Select(group => group.First())
            .ToArray();

    private static KeyGesture Gesture(Key key, KeyModifiers modifiers) =>
        new(key, modifiers);
}

public sealed class ShortcutGestureExtension : MarkupExtension
{
    public string Source { get; set; } = string.Empty;

    public string Id { get; set; } = string.Empty;

    public int Index { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var registry = (KeyboardShortcutRegistry)new StaticResourceExtension(typeof(KeyboardShortcutRegistry))
            .ProvideValue(serviceProvider)!;
        return registry.GesturesBySource[Source][Id][Index];
    }
}

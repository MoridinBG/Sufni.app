using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Sufni.App.KeyboardShortcuts;

public static class KeyboardShortcutRegistry
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

    internal static KeyModifiers CommandModifier { get; } =
        OperatingSystem.IsMacOS() || OperatingSystem.IsIOS()
            ? KeyModifiers.Meta
            : KeyModifiers.Control;

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>>> GesturesBySource { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, IReadOnlyList<KeyGesture>>>
        {
            [ShortcutConfiguration.MainWindow] = new Dictionary<string, IReadOnlyList<KeyGesture>>
            {
                [ShortcutConfiguration.CloseCurrentTab] = [Gesture(Key.W, CommandModifier)],
                [ShortcutConfiguration.RestoreClosedTab] = [Gesture(Key.T, CommandModifier | KeyModifiers.Shift)],
                [ShortcutConfiguration.SelectNextTab] =
                [
                    Gesture(Key.Tab, CommandModifier),
                    Gesture(Key.OemCloseBrackets, CommandModifier | KeyModifiers.Shift),
                ],
                [ShortcutConfiguration.SelectPreviousTab] =
                [
                    Gesture(Key.Tab, CommandModifier | KeyModifiers.Shift),
                    Gesture(Key.OemOpenBrackets, CommandModifier | KeyModifiers.Shift),
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
        };

    public static IReadOnlyList<KeyGesture> All { get; } =
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

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        KeyboardShortcutRegistry.GesturesBySource[Source][Id][Index];
}

using Avalonia.Input;

using Sufni.App.Shell.KeyboardShortcuts;
namespace Sufni.App.Tests.Shell.KeyboardShortcuts;

public class KeyboardShortcutRegistryTests
{
    [Fact]
    public void All_DeduplicatesGesturesRegisteredByMultipleSurfaces()
    {
        var deleteRegistrations = KeyboardShortcutRegistry.GesturesBySource.Values
            .SelectMany(bindingsById => bindingsById.Values)
            .SelectMany(gestures => gestures)
            .Count(gesture => Matches(gesture, Key.Delete, KeyModifiers.None));

        Assert.True(deleteRegistrations > 1);
        Assert.Equal(
            1,
            KeyboardShortcutRegistry.All.Count(gesture => Matches(gesture, Key.Delete, KeyModifiers.None)));
    }

    [Fact]
    public void PredefinedShortcuts_DefineExpectedGestures()
    {
        var commandModifier = KeyboardShortcutRegistry.CommandModifier;

        AssertGesture(
            Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.CloseCurrentTab),
            Key.W,
            commandModifier);
        AssertGesture(
            Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.RestoreClosedTab),
            Key.T,
            commandModifier | KeyModifiers.Shift);
        AssertGesture(
            Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.BikeImageDesktopView, KeyboardShortcutRegistry.ShortcutConfiguration.DeleteSelection),
            Key.Delete,
            KeyModifiers.None);
        AssertGesture(
            Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab),
            Key.Tab,
            KeyModifiers.Control);
        AssertGesture(
            Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab, 1),
            Key.OemCloseBrackets,
            commandModifier | KeyModifiers.Shift);
        AssertGesture(
            Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab),
            Key.Tab,
            KeyModifiers.Control | KeyModifiers.Shift);
        AssertGesture(
            Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab, 1),
            Key.OemOpenBrackets,
            commandModifier | KeyModifiers.Shift);
    }

    [Fact]
    public void CommandModifier_UsesMetaOnlyOnApplePlatforms()
    {
        var expected = OperatingSystem.IsMacOS() || OperatingSystem.IsIOS()
            ? KeyModifiers.Meta
            : KeyModifiers.Control;

        Assert.Equal(expected, KeyboardShortcutRegistry.CommandModifier);
    }

    [Fact]
    public void ShortcutGestureExtension_ResolvesGestureFromSourceAndId()
    {
        var extension = new ShortcutGestureExtension
        {
            Source = KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow,
            Id = KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab,
            Index = 1,
        };

        var gesture = Assert.IsType<KeyGesture>(extension.ProvideValue(serviceProvider: null!));

        AssertGesture(
            gesture,
            Key.OemCloseBrackets,
            KeyboardShortcutRegistry.CommandModifier | KeyModifiers.Shift);
    }

    private static bool Matches(KeyGesture gesture, Key key, KeyModifiers modifiers) =>
        gesture.Key == key &&
        gesture.KeyModifiers == modifiers;

    private static KeyGesture Shortcut(string source, string id, int index = 0) =>
        KeyboardShortcutRegistry.GesturesBySource[source][id][index];

    private static void AssertGesture(KeyGesture gesture, Key key, KeyModifiers modifiers)
    {
        Assert.Equal(key, gesture.Key);
        Assert.Equal(modifiers, gesture.KeyModifiers);
    }
}

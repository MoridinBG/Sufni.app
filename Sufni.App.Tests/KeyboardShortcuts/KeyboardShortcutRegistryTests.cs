using Avalonia.Input;
using Sufni.App.KeyboardShortcuts;

namespace Sufni.App.Tests.KeyboardShortcuts;

public class KeyboardShortcutRegistryTests
{
    [Fact]
    public void All_IncludesEveryUniquePredefinedGesture()
    {
        var commandModifier = KeyboardShortcutRegistry.CommandModifier;

        Assert.Equal(7, KeyboardShortcutRegistry.All.Count);
        Assert.Contains(KeyboardShortcutRegistry.All, gesture => Matches(gesture, Key.W, commandModifier));
        Assert.Contains(KeyboardShortcutRegistry.All, gesture => Matches(gesture, Key.T, commandModifier | KeyModifiers.Shift));
        Assert.Contains(KeyboardShortcutRegistry.All, gesture => Matches(gesture, Key.Delete, KeyModifiers.None));
        Assert.Contains(KeyboardShortcutRegistry.All, gesture => Matches(gesture, Key.Tab, commandModifier));
        Assert.Contains(KeyboardShortcutRegistry.All, gesture => Matches(gesture, Key.OemCloseBrackets, commandModifier | KeyModifiers.Shift));
        Assert.Contains(KeyboardShortcutRegistry.All, gesture => Matches(gesture, Key.Tab, commandModifier | KeyModifiers.Shift));
        Assert.Contains(KeyboardShortcutRegistry.All, gesture => Matches(gesture, Key.OemOpenBrackets, commandModifier | KeyModifiers.Shift));
    }

    [Fact]
    public void GesturesBySource_GroupsShortcutsByOwningSurfaceAndId()
    {
        var commandModifier = KeyboardShortcutRegistry.CommandModifier;

        Assert.Equal(3, KeyboardShortcutRegistry.GesturesBySource.Count);

        var mainWindow = KeyboardShortcutRegistry.GesturesBySource[KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow];
        Assert.Equal(4, mainWindow.Count);
        Assert.Collection(
            mainWindow[KeyboardShortcutRegistry.ShortcutConfiguration.CloseCurrentTab],
            gesture => AssertGesture(gesture, Key.W, commandModifier));
        Assert.Collection(
            mainWindow[KeyboardShortcutRegistry.ShortcutConfiguration.RestoreClosedTab],
            gesture => AssertGesture(gesture, Key.T, commandModifier | KeyModifiers.Shift));
        Assert.Collection(
            mainWindow[KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab],
            gesture => AssertGesture(gesture, Key.Tab, commandModifier),
            gesture => AssertGesture(gesture, Key.OemCloseBrackets, commandModifier | KeyModifiers.Shift));
        Assert.Collection(
            mainWindow[KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab],
            gesture => AssertGesture(gesture, Key.Tab, commandModifier | KeyModifiers.Shift),
            gesture => AssertGesture(gesture, Key.OemOpenBrackets, commandModifier | KeyModifiers.Shift));

        Assert.Collection(
            KeyboardShortcutRegistry.GesturesBySource[KeyboardShortcutRegistry.ShortcutConfiguration.BikeImageDesktopView][KeyboardShortcutRegistry.ShortcutConfiguration.DeleteSelection],
            gesture => AssertGesture(gesture, Key.Delete, KeyModifiers.None));
        Assert.Collection(
            KeyboardShortcutRegistry.GesturesBySource[KeyboardShortcutRegistry.ShortcutConfiguration.BikeImageControlsDesktopView][KeyboardShortcutRegistry.ShortcutConfiguration.DeleteSelection],
            gesture => AssertGesture(gesture, Key.Delete, KeyModifiers.None));
    }

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
            commandModifier);
        AssertGesture(
            Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab, 1),
            Key.OemCloseBrackets,
            commandModifier | KeyModifiers.Shift);
        AssertGesture(
            Shortcut(KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab),
            Key.Tab,
            commandModifier | KeyModifiers.Shift);
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

    [Fact]
    public void All_DoesNotRepeatGestures()
    {
        var duplicates = KeyboardShortcutRegistry.All
            .Select(gesture => new
            {
                gesture.Key,
                gesture.KeyModifiers,
            })
            .GroupBy(gesture => new { gesture.Key, gesture.KeyModifiers })
            .Where(group => group.Count() > 1)
            .ToArray();

        Assert.Empty(duplicates);
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

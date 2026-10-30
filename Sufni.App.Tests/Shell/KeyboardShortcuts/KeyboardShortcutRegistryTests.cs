using Avalonia.Input;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Harness;

using Sufni.App.Shell.KeyboardShortcuts;
namespace Sufni.App.Tests.Shell.KeyboardShortcuts;

public class KeyboardShortcutRegistryTests
{
    [Fact]
    public void All_DeduplicatesGesturesRegisteredByMultipleSurfaces()
    {
        var registry = new KeyboardShortcutRegistry(TestAppEnvironment.Create(UiLayoutProfile.Workspace).Input);
        var deleteRegistrations = registry.GesturesBySource.Values
            .SelectMany(bindingsById => bindingsById.Values)
            .SelectMany(gestures => gestures)
            .Count(gesture => Matches(gesture, Key.Delete, KeyModifiers.None));

        Assert.True(deleteRegistrations > 1);
        Assert.Equal(
            1,
            registry.All.Count(gesture => Matches(gesture, Key.Delete, KeyModifiers.None)));
    }

    [Theory]
    [InlineData(CommandModifier.Control, KeyModifiers.Control)]
    [InlineData(CommandModifier.Meta, KeyModifiers.Meta)]
    public void PredefinedShortcuts_UseInputCapability(CommandModifier modifier, KeyModifiers commandModifier)
    {
        var input = TestAppEnvironment.Create(UiLayoutProfile.Workspace).Input with { CommandModifier = modifier };
        var registry = new KeyboardShortcutRegistry(input);

        AssertGesture(
            Shortcut(registry, KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.CloseCurrentTab),
            Key.W,
            commandModifier);
        AssertGesture(
            Shortcut(registry, KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.RestoreClosedTab),
            Key.T,
            commandModifier | KeyModifiers.Shift);
        AssertGesture(
            Shortcut(registry, KeyboardShortcutRegistry.ShortcutConfiguration.BikeImageDesktopView, KeyboardShortcutRegistry.ShortcutConfiguration.DeleteSelection),
            Key.Delete,
            KeyModifiers.None);
        AssertGesture(
            Shortcut(registry, KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab),
            Key.Tab,
            KeyModifiers.Control);
        AssertGesture(
            Shortcut(registry, KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectNextTab, 1),
            Key.OemCloseBrackets,
            commandModifier | KeyModifiers.Shift);
        AssertGesture(
            Shortcut(registry, KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab),
            Key.Tab,
            KeyModifiers.Control | KeyModifiers.Shift);
        AssertGesture(
            Shortcut(registry, KeyboardShortcutRegistry.ShortcutConfiguration.MainWindow, KeyboardShortcutRegistry.ShortcutConfiguration.SelectPreviousTab, 1),
            Key.OemOpenBrackets,
            commandModifier | KeyModifiers.Shift);
    }

    private static bool Matches(KeyGesture gesture, Key key, KeyModifiers modifiers) =>
        gesture.Key == key &&
        gesture.KeyModifiers == modifiers;

    private static KeyGesture Shortcut(KeyboardShortcutRegistry registry, string source, string id, int index = 0) =>
        registry.GesturesBySource[source][id][index];

    private static void AssertGesture(KeyGesture gesture, Key key, KeyModifiers modifiers)
    {
        Assert.Equal(key, gesture.Key);
        Assert.Equal(modifiers, gesture.KeyModifiers);
    }
}

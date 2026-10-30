using Avalonia.Input;
using Avalonia.Input.Raw;
using Sufni.App.macOS;

namespace Sufni.App.Tests.Platform.macOS;

public class RawTabShortcutHandlerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TabKeyUp_SelectsOneTab_WithOrWithoutPrecedingKeyDown(bool hasKeyDown)
    {
        var selectedTab = 0;
        var handler = new RawTabShortcutHandler((_, _) => { selectedTab++; return true; });

        if (hasKeyDown)
        {
            Assert.True(handler.Handle(RawKeyEventType.KeyDown, Key.Tab, KeyModifiers.Control));
        }
        Assert.True(handler.Handle(RawKeyEventType.KeyUp, Key.Tab, KeyModifiers.Control));

        Assert.Equal(1, selectedTab);
    }

    [Fact]
    public void OtherKeys_AndUnregisteredTabModifiers_RemainUnhandled()
    {
        var selectedTab = 0;
        var handler = new RawTabShortcutHandler((_, modifiers) =>
        {
            if (modifiers != KeyModifiers.Control) return false;
            selectedTab++;
            return true;
        });

        Assert.False(handler.Handle(RawKeyEventType.KeyDown, Key.A, KeyModifiers.Control));
        Assert.False(handler.Handle(RawKeyEventType.KeyUp, Key.Tab, KeyModifiers.None));
        Assert.Equal(0, selectedTab);
    }
}

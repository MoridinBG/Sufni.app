using Avalonia.Headless.XUnit;
using Sufni.App.Views.Shared;

namespace Sufni.App.Tests.Views.Shared;

public class ItemListStatusOverlayTests
{
    [AvaloniaFact]
    public void Overlay_DeclaresElevatedZIndex_SoHostChildOrderDoesNotMatter()
    {
        var overlay = new ItemListStatusOverlay();

        Assert.Equal(10, overlay.ZIndex);
    }
}

using Avalonia.Headless.XUnit;

using Sufni.App.Shared.Views.Overlays;
namespace Sufni.App.Tests.Views.Shared;

[Collection("Ui")]
public class ItemListStatusOverlayTests
{
    [AvaloniaFact]
    public void Overlay_DeclaresElevatedZIndex_SoHostChildOrderDoesNotMatter()
    {
        var overlay = new ItemListStatusOverlay();

        Assert.Equal(10, overlay.ZIndex);
    }
}

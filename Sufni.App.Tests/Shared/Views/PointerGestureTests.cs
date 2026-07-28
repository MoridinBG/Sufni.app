using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Shared.Views.Input;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shared.Views;

[Collection("Ui")]
public class PointerGestureTests
{
    [AvaloniaFact]
    public async Task SupportsTouchLongPressContextMenu_InheritsFromRoot()
    {
        var child = new Border();
        var root = new Grid
        {
            Children = { child },
        };
        PointerGesture.SetSupportsTouchLongPressContextMenu(root, true);
        var window = await ViewTestHelpers.ShowViewAsync(root);

        try
        {
            Assert.True(PointerGesture.SupportsTouchLongPressContextMenu(child));
        }
        finally
        {
            window.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}

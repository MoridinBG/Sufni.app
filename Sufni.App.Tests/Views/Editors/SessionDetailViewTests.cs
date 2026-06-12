using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Editors;

[Collection("Ui")]
public class SessionDetailViewTests
{
    [AvaloniaFact]
    public async Task SessionDetailView_RemovesBalancePage_WhenBalanceDataIsUnavailable()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountMobileAsync(
            loadResult: context.CreateMobileLoadedState(includeBalance: false));

        var tabHeaders = mounted.View.GetVisualDescendants()
            .OfType<ItemsControl>()
            .FirstOrDefault(c => c.Name == "TabHeaders");

        Assert.NotNull(tabHeaders);
        Assert.Equal(mounted.Editor.Pages.Count, tabHeaders!.ItemCount);
        Assert.DoesNotContain(mounted.Editor.Pages, page => page is BalancePageViewModel);
    }

    [AvaloniaFact]
    public async Task SessionDetailView_BindsEditorChromeThroughTheMobileWorkspace()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountMobileAsync();

        // Error bar: hidden without errors, shows the message once one lands.
        var errorBar = mounted.View.GetVisualDescendants()
            .OfType<ErrorMessagesBar>()
            .FirstOrDefault();
        Assert.NotNull(errorBar);
        Assert.False(errorBar!.IsVisible);

        mounted.Editor.ErrorMessages.Add("Something went wrong");
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.True(errorBar.IsVisible);

        // Back button: wired to the editor's command, not left commandless.
        var backButton = mounted.View.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Name == "BackButton");
        Assert.NotNull(backButton);
        Assert.NotNull(backButton!.Command);
        Assert.True(backButton.IsEffectivelyEnabled);
    }
}

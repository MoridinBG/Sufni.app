using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Detail.Views.Editors;

[Collection("Ui")]
public class SessionDetailViewTests
{
    [AvaloniaFact]
    public async Task SessionDetailView_RemovesBalancePage_WhenBalanceDataIsUnavailable()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountMobileAsync(
            loadResult: context.CreateLoadedState(includeBalance: false));

        var carousel = mounted.View.GetVisualDescendants()
            .OfType<CarouselPage>()
            .FirstOrDefault(c => c.Name == "SessionCarouselPage");
        var pager = mounted.View.GetVisualDescendants()
            .OfType<PipsPager>()
            .FirstOrDefault(c => c.Name == "SessionPipsPager");

        Assert.NotNull(carousel);
        Assert.Same(mounted.Editor.Pages, carousel!.ItemsSource);
        Assert.NotNull(pager);
        Assert.Equal(mounted.Editor.MobileWorkspace.PageCount, pager!.NumberOfPages);
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

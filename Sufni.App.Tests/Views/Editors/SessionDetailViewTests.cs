using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.SessionDetails;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Editors;

public class SessionDetailViewTests
{
    [AvaloniaFact]
    public async Task SessionDetailView_RendersExpectedContainerControls_AndPageSet()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountMobileAsync();

        var tabHeaders = mounted.View.FindControl<ItemsControl>("TabHeaders");
        Assert.NotNull(tabHeaders);
        Assert.Equal(4, tabHeaders!.ItemCount);
        Assert.Equal(["Spring", "Damper", "Balance", "Notes"], mounted.Editor.Pages.Select(page => page.DisplayName));
        Assert.True(mounted.Editor.Pages.OfType<PageViewModelBase>().First(page => page.DisplayName == "Spring").Selected);
        Assert.All(mounted.Editor.Pages.OfType<PageViewModelBase>().Where(page => page.DisplayName != "Spring"), page => Assert.False(page.Selected));

        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<EditableTitle>().SingleOrDefault());
        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<ErrorMessagesBar>().SingleOrDefault());
        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<CommonButtonLine>().SingleOrDefault());
    }

    [AvaloniaFact]
    public async Task SessionDetailView_RemovesBalancePage_WhenBalanceDataIsUnavailable()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountMobileAsync(loadResult: context.CreateMobileLoadedState(includeBalance: false));

        var tabHeaders = mounted.View.FindControl<ItemsControl>("TabHeaders");
        Assert.NotNull(tabHeaders);
        Assert.Equal(3, tabHeaders!.ItemCount);
        Assert.Equal(["Spring", "Damper", "Notes"], mounted.Editor.Pages.Select(page => page.DisplayName));
        Assert.DoesNotContain(mounted.Editor.Pages, page => page.DisplayName == "Balance");
    }

    [AvaloniaFact]
    public async Task SessionDetailView_ReplacesMobileShellWithScreenError_WhenLoadFails()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountMobileAsync(
            loadResult: new SessionMobileLoadResult.Failed("boom"));

        var errorHeading = mounted.View.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(textBlock => textBlock.Text == "Could not load session");
        var errorText = mounted.View.GetVisualDescendants()
            .OfType<TextBlock>()
            .FirstOrDefault(textBlock => textBlock.Text == "Could not load session data: boom");

        Assert.True(mounted.Editor.ScreenState.IsError);
        Assert.NotNull(errorHeading);
        Assert.NotNull(errorText);
    }
}
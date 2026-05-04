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

        var tabHeaders = mounted.View.GetVisualDescendants()
            .OfType<ItemsControl>()
            .FirstOrDefault(c => c.Name == "TabHeaders");
        Assert.NotNull(tabHeaders);
        Assert.Equal(9, tabHeaders!.ItemCount);
        Assert.Equal(["Graph", "Spring", "Strokes", "Damper", "Balance", "Vibration", "Analysis", "Notes", "Preferences"], mounted.Editor.Pages.Select(page => page.DisplayName));
        Assert.True(mounted.Editor.Pages.OfType<PageViewModelBase>().First(page => page.DisplayName == "Graph").Selected);
        Assert.All(mounted.Editor.Pages.OfType<PageViewModelBase>().Where(page => page.DisplayName != "Graph"), page => Assert.False(page.Selected));

        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<EditableTitle>().SingleOrDefault());
        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<ErrorMessagesBar>().SingleOrDefault());
        Assert.NotNull(mounted.View.GetVisualDescendants().OfType<CommonButtonLine>().SingleOrDefault());
    }

    [AvaloniaFact]
    public async Task SessionDetailView_RemovesBalancePage_WhenBalanceDataIsUnavailable()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountMobileAsync(loadResult: context.CreateMobileLoadedState(includeBalance: false));

        var tabHeaders = mounted.View.GetVisualDescendants()
            .OfType<ItemsControl>()
            .FirstOrDefault(c => c.Name == "TabHeaders");
        Assert.NotNull(tabHeaders);
        Assert.Equal(8, tabHeaders!.ItemCount);
        Assert.Equal(["Graph", "Spring", "Strokes", "Damper", "Vibration", "Analysis", "Notes", "Preferences"], mounted.Editor.Pages.Select(page => page.DisplayName));
        Assert.DoesNotContain(mounted.Editor.Pages, page => page.DisplayName == "Balance");
    }

    [AvaloniaFact]
    public async Task SessionDetailView_HidesExtendedStatistics_WhenCachedTelemetryIsUnavailable()
    {
        var context = new SessionDetailViewTestContext();

        await using var mounted = await context.MountMobileAsync(
            loadResult: context.CreateMobileLoadedState(includeTelemetry: false));

        var springPage = mounted.Editor.Pages.OfType<SpringPageViewModel>().Single();
        var damperPage = mounted.Editor.Pages.OfType<DamperPageViewModel>().Single();

        Assert.True(springPage.FrontHistogramState.IsReady);
        Assert.True(damperPage.FrontHistogramState.IsReady);
        Assert.True(mounted.Editor.FrontStatisticsState.IsHidden);
        Assert.True(mounted.Editor.RearStatisticsState.IsHidden);
        Assert.True(mounted.Editor.SessionAnalysis.State.IsHidden);
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

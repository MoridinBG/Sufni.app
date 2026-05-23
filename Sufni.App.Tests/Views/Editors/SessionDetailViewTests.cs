using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.ViewModels.SessionPages;

namespace Sufni.App.Tests.Views.Editors;

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
}

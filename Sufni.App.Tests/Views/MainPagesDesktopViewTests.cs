using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.DesktopViews;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;

namespace Sufni.App.Tests.Views;

public class MainPagesDesktopViewTests
{
    [AvaloniaFact]
    public async Task MainPagesDesktopView_ContainsLiveDaqsPrimaryTab()
    {
        TestApp.SetIsDesktop(true);
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates();

        var viewModel = new MainPagesViewModel
        {
            PairingServerViewModel = new PairingServerViewModel(Substitute.For<IPairingServerCoordinator>())
        };

        var view = new MainPagesDesktopView
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var liveTab = view.FindControl<TabItem>("LiveDaqsTabItem");
            Assert.NotNull(liveTab);
            Assert.True(liveTab!.IsVisible);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
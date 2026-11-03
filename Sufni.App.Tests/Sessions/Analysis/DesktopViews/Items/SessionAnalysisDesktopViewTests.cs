using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.Sessions.Analysis.DesktopViews.Items;
using Sufni.App.Sessions.Analysis.Views.Controls;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;

namespace Sufni.App.Tests.Sessions.Analysis.DesktopViews.Items;

[Collection("Ui")]
public class SessionAnalysisDesktopViewTests
{
    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_OmitsPositionVelocityTab_WhenWorkspaceDoesNotOfferIt()
    {
        using var workspace = CreateWorkspace();
        await using var mounted = await MountAsync(workspace);

        Assert.DoesNotContain("Position/velocity", GetTabHeaders(mounted.View));
    }

    private static TestSessionAnalysisWorkspace CreateWorkspace() =>
        new(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalance: true,
            hasReboundBalance: true);

    private static async Task<MountedView<SessionAnalysisDesktopView>> MountAsync(TestSessionAnalysisWorkspace workspace)
    {
        ViewTestHelpers.EnsureSessionDetailViewSetup(isDesktop: true);

        var view = new SessionAnalysisDesktopView
        {
            DataContext = workspace,
        };

        var container = new Border { Child = view };
        var host = await ViewTestHelpers.ShowViewAsync(container);
        return new MountedView<SessionAnalysisDesktopView>(host, view);
    }

    private static List<string> GetTabHeaders(SessionAnalysisDesktopView view)
    {
        var tabControl = view.FindControl<TabStrip>("TabControl")!;
        return tabControl.Items
            .OfType<TabItem>()
            .Select(item => item.Header?.ToString() ?? "")
            .ToList();
    }

}

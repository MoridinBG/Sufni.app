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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionAnalysisDesktopView_OffersBikeResponseTab_OnlyWhenWorkspaceHasIt(bool hasBikeResponse)
    {
        using var workspace = CreateWorkspace();
        workspace.HasBikeResponse = hasBikeResponse;
        await using var mounted = await MountAsync(workspace);

        Assert.Equal(hasBikeResponse, GetTabHeaders(mounted.View).Contains("Bike response"));
        Assert.False(mounted.View.FindControl<Grid>("BikeResponse")!.IsVisible);
    }

    [AvaloniaTheory]
    [InlineData("Ground & comfort")]
    [InlineData("Bike response")]
    public async Task SessionAnalysisDesktopView_TabsThatReadTheInsightsResultRequestIt(string header)
    {
        using var workspace = CreateWorkspace();
        workspace.HasBikeResponse = true;
        await using var mounted = await MountAsync(workspace);
        var tabs = mounted.View.FindControl<TabStrip>("TabControl")!;

        tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(item => Equals(item.Header, header));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(1, workspace.InsightsRequests);
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

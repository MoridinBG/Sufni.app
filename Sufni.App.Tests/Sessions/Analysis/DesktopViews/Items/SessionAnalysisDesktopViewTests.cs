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
    public async Task SessionAnalysisDesktopView_ShowsPositionVelocityTabAfterDamping_WhenWorkspaceOffersIt()
    {
        var workspace = CreateWorkspace();
        workspace.PositionVelocity = new PositionVelocityPageViewModel(workspace);

        await using var mounted = await MountAsync(workspace);

        var headers = GetTabHeaders(mounted.View);
        Assert.Equal(headers.IndexOf("Damping") + 1, headers.IndexOf("Position/velocity"));
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_OmitsPositionVelocityTab_WhenWorkspaceDoesNotOfferIt()
    {
        await using var mounted = await MountAsync(CreateWorkspace());

        Assert.DoesNotContain("Position/velocity", GetTabHeaders(mounted.View));
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_SelectingPositionVelocity_ActivatesOnlyItsAnalysisDemand()
    {
        var workspace = CreateWorkspace();
        workspace.PositionVelocity = new PositionVelocityPageViewModel(workspace);
        await using var mounted = await MountAsync(workspace);

        await SelectTabAsync(mounted.View, "Position/velocity");

        var positionVelocityHost = mounted.View.FindControl<Grid>("PositionVelocity")!
            .GetVisualDescendants()
            .OfType<PositionVelocityAnalysisHost>()
            .Single();
        Assert.True(positionVelocityHost.IsAnalysisDemandActive);
        Assert.All(
            mounted.View.FindControl<Grid>("SpringRate")!.GetVisualDescendants().OfType<AnalysisHostBase>(),
            host => Assert.False(host.IsAnalysisDemandActive));
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_SuspendsSelectedTabDemand_WhenOuterViewHidden()
    {
        var workspace = CreateWorkspace();
        await using var mounted = await MountAsync(workspace);
        var selectedHosts = mounted.View.FindControl<Grid>("SpringRate")!
            .GetVisualDescendants()
            .OfType<AnalysisHostBase>()
            .ToArray();

        Assert.NotEmpty(selectedHosts);
        Assert.All(selectedHosts, host => Assert.True(host.IsAnalysisDemandActive));

        mounted.Container.IsVisible = false;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.All(selectedHosts, host => Assert.False(host.IsAnalysisDemandActive));

        mounted.Container.IsVisible = true;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.All(selectedHosts, host => Assert.True(host.IsAnalysisDemandActive));
    }

    private static TestSessionAnalysisWorkspace CreateWorkspace() =>
        new(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalance: true,
            hasReboundBalance: true);

    private static async Task<MountedSessionAnalysisDesktopView> MountAsync(TestSessionAnalysisWorkspace workspace)
    {
        ViewTestHelpers.EnsureSessionDetailViewSetup(isDesktop: true);

        var view = new SessionAnalysisDesktopView
        {
            DataContext = workspace,
        };

        var container = new Border { Child = view };
        var host = await ViewTestHelpers.ShowViewAsync(container);
        return new MountedSessionAnalysisDesktopView(host, container, view);
    }

    private static List<string> GetTabHeaders(SessionAnalysisDesktopView view)
    {
        var tabControl = view.FindControl<TabStrip>("TabControl")!;
        return tabControl.Items
            .OfType<TabItem>()
            .Select(item => item.Header?.ToString() ?? "")
            .ToList();
    }

    private static async Task SelectTabAsync(SessionAnalysisDesktopView view, string header)
    {
        var tabControl = view.FindControl<TabStrip>("TabControl")!;
        var index = tabControl.Items
            .OfType<TabItem>()
            .Select((item, itemIndex) => (item, itemIndex))
            .Single(entry => string.Equals(entry.item.Header?.ToString(), header, StringComparison.Ordinal))
            .itemIndex;

        tabControl.SelectedIndex = index;
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}

internal sealed class MountedSessionAnalysisDesktopView(
    Window host,
    Border container,
    SessionAnalysisDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public Border Container { get; } = container;
    public SessionAnalysisDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}

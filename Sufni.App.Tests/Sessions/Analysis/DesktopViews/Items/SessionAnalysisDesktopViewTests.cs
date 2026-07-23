using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.Sessions.Analysis.DesktopViews.Items;
using Sufni.App.Sessions.Analysis.Views.Controls;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;

namespace Sufni.App.Tests.Sessions.Analysis.DesktopViews.Items;

[Collection("Ui")]
public class SessionAnalysisDesktopViewTests
{
    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_ComposesBuiltInAndExtensionTabs()
    {
        var workspace = CreateWorkspace();
        workspace.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution());

        await using var mounted = await MountAsync(workspace);

        Assert.Contains("Spring rate", GetTabHeaders(mounted.View));
        Assert.Contains("Extension tab", GetTabHeaders(mounted.View));
        Assert.True(mounted.View.FindControl<Grid>("SpringRate")!.IsVisible);

        await SelectTabAsync(mounted.View, "Extension tab");

        var extensionContent = GetExtensionAnalysisTabContent(mounted.View);
        Assert.True(extensionContent.IsVisible);
        Assert.False(mounted.View.FindControl<Grid>("SpringRate")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_ExtensionTab_IsMaterializedLazilyAndDisposedWhenRemoved()
    {
        var createdCount = 0;
        var workspace = CreateWorkspace();
        var viewModel = new DisposableContributionViewModel();
        workspace.ExtensionSlots.AnalysisTabs.Add(new RecordedSessionAnalysisTabContribution(
            "extension",
            "disposable-tab",
            Order: 0,
            "Disposable tab",
            RequestedIndex: 3,
            () =>
            {
                createdCount++;
                return viewModel;
            }));

        await using var mounted = await MountAsync(workspace);

        Assert.Equal(0, createdCount);

        await SelectTabAsync(mounted.View, "Disposable tab");

        Assert.Equal(1, createdCount);
        Assert.False(viewModel.IsDisposed);

        workspace.ExtensionSlots.AnalysisTabs.Clear();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(1, viewModel.DisposeCount);
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

    private static RecordedSessionAnalysisTabContribution CreateAnalysisTabContribution()
    {
        return new RecordedSessionAnalysisTabContribution(
            "extension",
            "extension-tab",
            Order: 0,
            "Extension tab",
            RequestedIndex: 3,
            () => new TestContributionViewModel
            {
                Name = "ExtensionAnalysisTabContent",
                Content = new TextBlock { Text = "Extension analysis" },
            });
    }

    private static IReadOnlyList<string> GetTabHeaders(SessionAnalysisDesktopView view)
    {
        var tabControl = view.FindControl<TabStrip>("TabControl")!;
        return tabControl.Items
            .OfType<TabItem>()
            .Select(item => item.Header?.ToString() ?? "")
            .ToArray();
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

    private static TestContributionViewModel GetExtensionAnalysisTabContent(SessionAnalysisDesktopView view)
    {
        var analysisContentHost = view.FindControl<ItemsControl>("AnalysisContentHost")!;
        return analysisContentHost.Items
            .OfType<TestContributionViewModel>()
            .Single(view => view.Name == "ExtensionAnalysisTabContent");
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

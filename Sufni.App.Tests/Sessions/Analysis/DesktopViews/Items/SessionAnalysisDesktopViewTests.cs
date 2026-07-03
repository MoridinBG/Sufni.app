using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Analysis.DesktopViews.Items;
using Sufni.App.Extensibility.Views;
using Sufni.App.Sessions.Insights.Views.Items;
using Sufni.App.Sessions.Plots.Views.Plots;
using Sufni.App.Sessions.Analysis.Views.Controls;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Analysis.DesktopViews.Items;

[Collection("Ui")]
public class SessionAnalysisDesktopViewTests
{
    [AvaloniaFact]
    public void SessionAnalysisDesktopView_HidesUnselectedBuiltInSectionsBeforeLoaded()
    {
        ViewTestHelpers.EnsureSessionDetailViewSetup(isDesktop: true);
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        var view = new SessionAnalysisDesktopView
        {
            DataContext = workspace,
        };

        Assert.True(view.FindControl<Grid>("SpringRate")!.IsVisible);
        Assert.False(view.FindControl<Grid>("Strokes")!.IsVisible);
        Assert.False(view.FindControl<Grid>("Damping")!.IsVisible);
        Assert.False(view.FindControl<Grid>("Balance")!.IsVisible);
        Assert.False(view.FindControl<Grid>("Vibration")!.IsVisible);
        Assert.False(view.FindControl<Grid>("Analysis")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_ShowsSpringSectionInitially_WhenFrontAndRearAnalysisAreAvailable()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        var springRate = mounted.View.FindControl<Grid>("SpringRate");
        var damping = mounted.View.FindControl<Grid>("Damping");
        var balance = mounted.View.FindControl<Grid>("Balance");
        var analysis = mounted.View.FindControl<Grid>("Analysis");

        Assert.NotNull(springRate);
        Assert.NotNull(damping);
        Assert.NotNull(balance);
        Assert.NotNull(analysis);

        Assert.True(springRate!.IsVisible);
        Assert.False(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
        Assert.False(analysis!.IsVisible);
        Assert.Equal(
            2,
            springRate.GetVisualDescendants()
                .OfType<TravelAnalysisHost>()
                .Count(host => host.PresentationState.ReservesLayout && host.ShowFrequencyDistribution));
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_SelectionUpdatesBuiltInAnalysisDemand()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true,
            hasFrontForkVibration: true,
            hasFrontFrameVibration: true,
            hasRearForkVibration: true,
            hasRearFrameVibration: true);

        await using var mounted = await MountAsync(workspace);

        AssertAnalysisDemand(mounted.View, "SpringRate", expected: true);
        AssertAnalysisDemand(mounted.View, "Strokes", expected: false);
        AssertAnalysisDemand(mounted.View, "Damping", expected: false);
        AssertAnalysisDemand(mounted.View, "Balance", expected: false);
        AssertAnalysisDemand(mounted.View, "Vibration", expected: false);

        await SelectTabAsync(mounted.View, "Damping");

        AssertAnalysisDemand(mounted.View, "SpringRate", expected: false);
        AssertAnalysisDemand(mounted.View, "Damping", expected: true);
        AssertAnalysisDemand(mounted.View, "Balance", expected: false);
        AssertAnalysisDemand(mounted.View, "Vibration", expected: false);

        await SelectTabAsync(mounted.View, "Insights");

        AssertAnalysisDemand(mounted.View, "SpringRate", expected: false);
        AssertAnalysisDemand(mounted.View, "Damping", expected: false);
        AssertAnalysisDemand(mounted.View, "Balance", expected: false);
        AssertAnalysisDemand(mounted.View, "Vibration", expected: false);
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_RendersAnalysisBannerContributions()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);
        workspace.ExtensionSlots.AnalysisBanners.Add(new RecordedSessionAnalysisBannerContribution(
            "extension",
            "analysis-banner",
            Order: 0,
            new TestContributionViewModel
            {
                Content = new TextBlock { Name = "DesktopAnalysisBanner", Text = "Analysis banner" },
            }));

        await using var mounted = await MountAsync(workspace);

        var contributionHost = Assert.Single(
            mounted.View.GetVisualDescendants().OfType<RecordedSessionAnalysisContributionsView>());
        Assert.NotNull(contributionHost);
        AssertContributionText(mounted.View, "DesktopAnalysisBanner", "Analysis banner");
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_RendersContributedAnalysisTabBeforeMatchingBuiltInTab()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);
        workspace.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution("extension-tab"));

        await using var mounted = await MountAsync(workspace);

        Assert.Equal(
            [
                "Spring rate",
                "Strokes",
                "Damping",
                "Extension tab",
                "Balance",
                "Vibration",
                "Insights",
            ],
            GetTabHeaders(mounted.View));
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_SelectingContributedAnalysisTab_ShowsExtensionContentOnly()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);
        workspace.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution("extension-tab"));

        await using var mounted = await MountAsync(workspace);
        await SelectTabAsync(mounted.View, "Extension tab");

        var extensionContent = GetExtensionAnalysisTabContent(mounted.View);
        Assert.True(extensionContent.IsVisible);
        Assert.False(mounted.View.FindControl<Grid>("SpringRate")!.IsVisible);
        Assert.False(mounted.View.FindControl<Grid>("Strokes")!.IsVisible);
        Assert.False(mounted.View.FindControl<Grid>("Damping")!.IsVisible);
        Assert.False(mounted.View.FindControl<Grid>("Balance")!.IsVisible);
        Assert.False(mounted.View.FindControl<Grid>("Vibration")!.IsVisible);
        Assert.False(mounted.View.FindControl<Grid>("Analysis")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_DoesNotMaterializeContributedAnalysisTabUntilSelected()
    {
        var createdCount = 0;
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);
        workspace.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution(
            "extension-tab",
            onCreate: () => createdCount++));

        await using var mounted = await MountAsync(workspace);

        Assert.Equal(0, createdCount);

        await SelectTabAsync(mounted.View, "Extension tab");

        Assert.Equal(1, createdCount);
        Assert.NotNull(GetExtensionAnalysisTabContent(mounted.View));
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_RemovingSelectedContributedAnalysisTab_FallsBackToSpring()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);
        workspace.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution("extension-tab"));

        await using var mounted = await MountAsync(workspace);
        await SelectTabAsync(mounted.View, "Extension tab");

        workspace.ExtensionSlots.AnalysisTabs.Clear();
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(
            ["Spring rate", "Strokes", "Damping", "Balance", "Vibration", "Insights"],
            GetTabHeaders(mounted.View));
        Assert.True(mounted.View.FindControl<Grid>("SpringRate")!.IsVisible);
        var analysisContentHost = mounted.View.FindControl<ItemsControl>("AnalysisContentHost")!;
        Assert.DoesNotContain(
            analysisContentHost.Items.OfType<TestContributionViewModel>(),
            view => view.Name == "ExtensionAnalysisTabContent");
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_SelectingBuiltInTab_StillDisplaysBuiltInPane_WhenExtensionTabsExist()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);
        workspace.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution("extension-tab"));

        await using var mounted = await MountAsync(workspace);
        await SelectTabAsync(mounted.View, "Extension tab");
        var extensionContent = GetExtensionAnalysisTabContent(mounted.View);
        await SelectTabAsync(mounted.View, "Balance");

        Assert.False(mounted.View.FindControl<Grid>("SpringRate")!.IsVisible);
        Assert.False(mounted.View.FindControl<Grid>("Damping")!.IsVisible);
        Assert.True(mounted.View.FindControl<Grid>("Balance")!.IsVisible);
        Assert.False(extensionContent.IsVisible);
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_ReusesContributedAnalysisTabContentAcrossRebuilds()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);
        workspace.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution("extension-tab"));

        await using var mounted = await MountAsync(workspace);
        await SelectTabAsync(mounted.View, "Extension tab");
        var firstContent = GetExtensionAnalysisTabContent(mounted.View);

        workspace.ExtensionSlots.AnalysisTabs.Add(CreateAnalysisTabContribution(
            "later-extension-tab",
            "Later extension tab",
            "LaterExtensionAnalysisTabContent"));
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Same(firstContent, GetExtensionAnalysisTabContent(mounted.View));
        Assert.DoesNotContain(
            mounted.View.FindControl<ItemsControl>("AnalysisContentHost")!.Items.OfType<TestContributionViewModel>(),
            view => view.Name == "LaterExtensionAnalysisTabContent");

        await SelectTabAsync(mounted.View, "Later extension tab");

        Assert.NotNull(GetExtensionAnalysisTabContent(mounted.View, "LaterExtensionAnalysisTabContent"));
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_UsesSingleScrollableAnalysisPanel()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        var scrollViewer = mounted.View.FindControl<ScrollViewer>("AnalysisPaneScrollViewer");
        var scrollablePanel = mounted.View.FindControl<StackPanel>("AnalysisScrollablePanel");
        var strokes = mounted.View.FindControl<Grid>("Strokes");

        Assert.NotNull(scrollViewer);
        Assert.NotNull(scrollablePanel);
        Assert.NotNull(strokes);
        Assert.Equal(ScrollBarVisibility.Disabled, scrollViewer!.HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, scrollViewer.VerticalScrollBarVisibility);
        Assert.Same(scrollablePanel, scrollViewer.Content);
        Assert.DoesNotContain(strokes!.GetVisualDescendants(), control => control is ScrollViewer);
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_UsesNaturalPlotHeights_ForAnalysisTabs()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true,
            hasFrontForkVibration: true,
            hasFrontFrameVibration: true,
            hasRearForkVibration: true,
            hasRearFrameVibration: true);

        await using var mounted = await MountAsync(workspace);

        var tabControl = mounted.View.FindControl<TabStrip>("TabControl")!;
        var springRate = mounted.View.FindControl<Grid>("SpringRate")!;
        var damping = mounted.View.FindControl<Grid>("Damping")!;
        var balance = mounted.View.FindControl<Grid>("Balance")!;
        var vibration = mounted.View.FindControl<Grid>("Vibration")!;

        tabControl.SelectedIndex = 0;
        await ViewTestHelpers.FlushDispatcherAsync();
        var springHosts = springRate.GetVisualDescendants()
            .OfType<TravelAnalysisHost>()
            .Where(host => host.ShowFrequencyDistribution)
            .ToArray();
        Assert.Equal(2, springHosts.Length);
        Assert.All(springHosts, host =>
        {
            Assert.Equal(320, host.TravelDistributionRowHeight.Value);
            Assert.Equal(GridUnitType.Pixel, host.TravelDistributionRowHeight.GridUnitType);
            Assert.Equal(240, host.TravelFrequencyDistributionRowHeight.Value);
            Assert.Equal(GridUnitType.Pixel, host.TravelFrequencyDistributionRowHeight.GridUnitType);
        });

        tabControl.SelectedIndex = 2;
        await ViewTestHelpers.FlushDispatcherAsync();
        var dampingHosts = damping.GetVisualDescendants()
            .OfType<DampingAnalysisHost>()
            .Where(host => host.PresentationState.ReservesLayout)
            .ToArray();
        Assert.Equal(2, dampingHosts.Length);
        Assert.All(dampingHosts, host => Assert.Equal(440, host.PlotHeight));

        tabControl.SelectedIndex = 3;
        await ViewTestHelpers.FlushDispatcherAsync();
        var balanceHosts = balance.GetVisualDescendants()
            .OfType<BalanceAnalysisHost>()
            .Where(host => host.PresentationState.ReservesLayout)
            .ToArray();
        Assert.Equal(2, balanceHosts.Length);
        Assert.All(balanceHosts, host => Assert.Equal(380, host.PlotHeight));

        tabControl.SelectedIndex = 4;
        await ViewTestHelpers.FlushDispatcherAsync();
        var vibrationHosts = vibration.GetVisualDescendants()
            .OfType<VibrationAnalysisHost>()
            .Where(host => host.PresentationState.ReservesLayout)
            .ToArray();
        Assert.Equal(4, vibrationHosts.Length);
        Assert.All(vibrationHosts, host =>
        {
            Assert.Equal(260, host.PlotRowHeight.Value);
            Assert.Equal(GridUnitType.Pixel, host.PlotRowHeight.GridUnitType);
        });
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_ShowsOnlyFrontDampingHosts_WhenOnlyFrontAnalysisIsAvailable()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: false,
            hasCompressionBalanceTelemetry: false,
            hasReboundBalanceTelemetry: false);

        await using var mounted = await MountAsync(workspace);

        var tabControl = mounted.View.FindControl<TabStrip>("TabControl");
        var springRate = mounted.View.FindControl<Grid>("SpringRate");
        var strokes = mounted.View.FindControl<Grid>("Strokes");
        var damping = mounted.View.FindControl<Grid>("Damping");
        var balance = mounted.View.FindControl<Grid>("Balance");
        var vibration = mounted.View.FindControl<Grid>("Vibration");

        Assert.NotNull(tabControl);
        Assert.NotNull(springRate);
        Assert.NotNull(strokes);
        Assert.NotNull(damping);
        Assert.NotNull(balance);
        Assert.NotNull(vibration);

        tabControl!.SelectedIndex = 2;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(springRate!.IsVisible);
        Assert.False(strokes!.IsVisible);
        Assert.True(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
        Assert.False(vibration!.IsVisible);
        var readyDampingHosts = damping.GetVisualDescendants()
            .OfType<DampingAnalysisHost>()
            .Where(host => host.PresentationState.ReservesLayout)
            .ToArray();
        var frontDampingHost = Assert.Single(readyDampingHosts);
        Assert.False(frontDampingHost.ShowTravelLegend);
        Assert.Single(
            damping.GetVisualDescendants().OfType<TravelPercentageLegend>(),
            legend => legend.IsVisible);
        Assert.Equal(workspace.DampingPercentages.FrontHscPercentage, frontDampingHost.HscPercentage);
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_AnalysisTab_BindsFindingsAndTargetProfile()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        var tabControl = mounted.View.FindControl<TabStrip>("TabControl");
        tabControl!.SelectedIndex = 5;
        await ViewTestHelpers.FlushDispatcherAsync();

        var analysis = mounted.View.FindControl<Grid>("Analysis");
        var springRate = mounted.View.FindControl<Grid>("SpringRate");
        var strokes = mounted.View.FindControl<Grid>("Strokes");
        var damping = mounted.View.FindControl<Grid>("Damping");
        var balance = mounted.View.FindControl<Grid>("Balance");
        var vibration = mounted.View.FindControl<Grid>("Vibration");
        var analysisView = analysis!.GetVisualDescendants().OfType<SessionInsightsView>().Single();
        var profileComboBox = analysisView.FindControl<ComboBox>("SessionInsightsTargetProfileComboBox");

        Assert.False(springRate!.IsVisible);
        Assert.False(strokes!.IsVisible);
        Assert.False(damping!.IsVisible);
        Assert.False(balance!.IsVisible);
        Assert.False(vibration!.IsVisible);
        Assert.True(analysis!.IsVisible);
        Assert.NotNull(profileComboBox);
        Assert.Equal(SessionInsightsTargetProfile.Trail, profileComboBox!.SelectedValue);

        profileComboBox.SelectedValue = SessionInsightsTargetProfile.Enduro;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(SessionInsightsTargetProfile.Enduro, workspace.SelectedSessionInsightsTargetProfile);
        var stepsItemsControl = analysisView.FindControl<ItemsControl>("SessionInsightsStepsItemsControl");
        Assert.NotNull(stepsItemsControl);
        var step = Assert.IsType<SessionInsightsStep>(Assert.Single(stepsItemsControl!.Items));
        Assert.Equal(SessionInsightsStepId.Sag, step.Id);
        var finding = Assert.Single(step.Findings);
        Assert.Equal("Travel use watch", finding.Title);
        Assert.Equal("The fork is not using much travel.", finding.Observation);
        Assert.Contains(step.Metrics, metric => metric.Label == "Max travel");
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_SelectingInsightsTabRequestsSessionInsights()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        Assert.Equal(0, workspace.SessionInsightsRequestCount);

        await SelectTabAsync(mounted.View, "Insights");

        Assert.Equal(1, workspace.SessionInsightsRequestCount);

        await SelectTabAsync(mounted.View, "Damping");

        Assert.Equal(1, workspace.SessionInsightsRequestCount);
    }

    [AvaloniaFact]
    public async Task SessionAnalysisDesktopView_BindsAnalysisModeSelectors()
    {
        var workspace = new SessionAnalysisWorkspaceStub(
            telemetryData: TestTelemetryData.CreateProcessed(),
            hasFrontAnalysis: true,
            hasRearAnalysis: true,
            hasCompressionBalanceTelemetry: true,
            hasReboundBalanceTelemetry: true);

        await using var mounted = await MountAsync(workspace);

        var travelMode = mounted.View.FindControl<ComboBox>("TravelDistributionModeComboBox");
        var balanceDisplacementMode = mounted.View.FindControl<ComboBox>("BalanceDisplacementModeComboBox");
        var balanceSpeedMode = mounted.View.FindControl<ComboBox>("BalanceSpeedModeComboBox");

        Assert.NotNull(travelMode);
        Assert.NotNull(balanceDisplacementMode);
        Assert.NotNull(balanceSpeedMode);

        Assert.Equal(TravelDistributionMode.ActiveSuspension, travelMode!.SelectedValue);
        Assert.Equal(BalanceDisplacementMode.Zenith, balanceDisplacementMode!.SelectedValue);
        Assert.Equal(BalanceSpeedMode.Both, balanceSpeedMode!.SelectedValue);

        travelMode.SelectedValue = TravelDistributionMode.DynamicSag;
        balanceDisplacementMode.SelectedValue = BalanceDisplacementMode.Travel;
        balanceSpeedMode.SelectedValue = BalanceSpeedMode.HighSpeed;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(TravelDistributionMode.DynamicSag, workspace.SelectedTravelDistributionMode);
        Assert.Equal(BalanceDisplacementMode.Travel, workspace.SelectedBalanceDisplacementMode);
        Assert.Equal(BalanceSpeedMode.HighSpeed, workspace.SelectedBalanceSpeedMode);
    }

    private static async Task<MountedSessionAnalysisDesktopView> MountAsync(SessionAnalysisWorkspaceStub workspace)
    {
        ViewTestHelpers.EnsureSessionDetailViewSetup(isDesktop: true);

        var view = new SessionAnalysisDesktopView
        {
            DataContext = workspace,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedSessionAnalysisDesktopView(host, view);
    }

    private static RecordedSessionAnalysisTabContribution CreateAnalysisTabContribution(
        string contributionId,
        string displayName = "Extension tab",
        string contentName = "ExtensionAnalysisTabContent",
        Action? onCreate = null)
    {
        return new RecordedSessionAnalysisTabContribution(
            "extension",
            contributionId,
            Order: 0,
            displayName,
            RequestedIndex: 3,
            () =>
            {
                onCreate?.Invoke();
                return new TestContributionViewModel
                {
                    Name = contentName,
                    Content = new TextBlock { Text = contentName },
                };
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

    private static TestContributionViewModel GetExtensionAnalysisTabContent(
        SessionAnalysisDesktopView view,
        string name = "ExtensionAnalysisTabContent")
    {
        var analysisContentHost = view.FindControl<ItemsControl>("AnalysisContentHost")!;
        return analysisContentHost.Items
            .OfType<TestContributionViewModel>()
            .Single(view => view.Name == name);
    }

    private static void AssertAnalysisDemand(SessionAnalysisDesktopView view, string sectionName, bool expected)
    {
        var hosts = view.FindControl<Grid>(sectionName)!
            .GetVisualDescendants()
            .OfType<AnalysisHostBase>()
            .ToArray();

        Assert.NotEmpty(hosts);
        Assert.All(hosts, host => Assert.Equal(expected, host.IsAnalysisDemandActive));
    }

    private static void AssertContributionText(Control root, string name, string text)
    {
        var textBlocks = root.GetVisualDescendants()
            .OfType<TextBlock>()
            .ToArray();
        var textBlock = textBlocks.SingleOrDefault(textBlock => textBlock.Name == name);
        Assert.True(
            textBlock is not null,
            $"Expected contribution text '{name}'. Actual text blocks: {string.Join(", ", textBlocks.Select(block => $"{block.Name}:{block.Text}"))}");
        Assert.Equal(text, textBlock!.Text);
    }

    private sealed class SessionAnalysisWorkspaceStub(
        TelemetryData telemetryData,
        bool hasFrontAnalysis,
        bool hasRearAnalysis,
        bool hasCompressionBalanceTelemetry,
        bool hasReboundBalanceTelemetry,
        bool hasFrontForkVibration = false,
        bool hasFrontFrameVibration = false,
        bool hasRearForkVibration = false,
        bool hasRearFrameVibration = false) : ISessionAnalysisWorkspace
    {
        public TelemetryData? TelemetryData { get; } = telemetryData;
        public TelemetryTimeRange? AnalysisRange => null;
        public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();
        public TravelDistributionMode SelectedTravelDistributionMode { get; set; } = TravelDistributionMode.ActiveSuspension;
        public BalanceDisplacementMode SelectedBalanceDisplacementMode { get; set; } = BalanceDisplacementMode.Zenith;
        public BalanceSpeedMode SelectedBalanceSpeedMode { get; set; } = BalanceSpeedMode.Both;
        public VelocityAverageMode SelectedVelocityAverageMode { get; set; } = VelocityAverageMode.SampleAveraged;
        public SessionInsightsTargetProfile SelectedSessionInsightsTargetProfile { get; set; } = SessionInsightsTargetProfile.Trail;
        public IReadOnlyList<TravelDistributionModeOption> TravelDistributionModeOptions { get; } =
        [
            new(TravelDistributionMode.ActiveSuspension, "Active suspension", "Uses only compression and rebound stroke samples. Best for travel use while the suspension is actively moving."),
            new(TravelDistributionMode.DynamicSag, "Dynamic sag", "Uses every selected travel sample. Best for ride height over the segment, including quiet or steady sections."),
        ];
        public IReadOnlyList<BalanceDisplacementModeOption> BalanceDisplacementModeOptions { get; } =
        [
            new(BalanceDisplacementMode.Zenith, "Zenith", "Plots each stroke at its deepest travel."),
            new(BalanceDisplacementMode.Travel, "Travel", "Plots each stroke by start-to-end travel distance."),
            new(BalanceDisplacementMode.Speed, "Speed", "Plots each stroke at the travel position where peak speed occurs."),
        ];
        public IReadOnlyList<BalanceSpeedModeOption> BalanceSpeedModeOptions { get; } =
        [
            new(BalanceSpeedMode.Both, "Both", "Uses all matching compression or rebound strokes."),
            new(BalanceSpeedMode.LowSpeed, "Low speed", "Uses strokes below the high-speed threshold."),
            new(BalanceSpeedMode.HighSpeed, "High speed", "Uses strokes at or above the high-speed threshold."),
        ];
        public IReadOnlyList<VelocityAverageModeOption> VelocityAverageModeOptions { get; } =
        [
            new(VelocityAverageMode.SampleAveraged, "Sample-averaged", "Counts every sample inside compression and rebound strokes. Best for where damping spent time."),
            new(VelocityAverageMode.StrokePeakAveraged, "Stroke-peak average", "Counts each stroke once by peak velocity and peak travel. Best for what events damping saw."),
        ];
        public IReadOnlyList<SessionInsightsTargetProfileOption> SessionInsightsTargetProfileOptions { get; } =
        [
            new(SessionInsightsTargetProfile.Weekend, "Weekend", "Uses conservative speed context for recreational pace and mixed terrain."),
            new(SessionInsightsTargetProfile.Trail, "Trail", "Uses general trail-riding speed context."),
            new(SessionInsightsTargetProfile.Enduro, "Enduro", "Uses faster rough-descending speed context."),
            new(SessionInsightsTargetProfile.DH, "DH", "Uses downhill-race speed context."),
        ];
        public string SessionAnalysisRangeText => "Full session";
        public string SessionAnalysisModesText => "Travel: Active suspension  Velocity: Sample-averaged  Balance: Zenith / Both";
        public SurfacePresentationState FrontAnalysisState { get; } = hasFrontAnalysis
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState RearAnalysisState { get; } = hasRearAnalysis
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState CompressionBalanceState { get; } = hasCompressionBalanceTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState ReboundBalanceState { get; } = hasReboundBalanceTelemetry
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState FrontForkVibrationState { get; } = hasFrontForkVibration
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState FrontFrameVibrationState { get; } = hasFrontFrameVibration
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState RearForkVibrationState { get; } = hasRearForkVibration
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SurfacePresentationState RearFrameVibrationState { get; } = hasRearFrameVibration
            ? SurfacePresentationState.Ready
            : SurfacePresentationState.Hidden;
        public SessionDampingPercentages DampingPercentages { get; } = new(10, 20, 30, 40, 50, 60, 70, 80);
        public DampingSpeedCutoffs DampingSpeedCutoffs { get; } = DampingSpeedCutoffs.Default;
        public DampingSpeedCutoffs PlotDampingSpeedCutoffs => DampingSpeedCutoffs;
        public bool CanEditDampingSpeedCutoffs => true;
        public int SessionInsightsRequestCount { get; private set; }
        public IRelayCommand<TelemetryRangeSelection?> SelectAnalysisRangeCommand { get; } =
            new RelayCommand<TelemetryRangeSelection?>(_ => { });
        public TelemetryRangeSelection? ActiveFrontAnalysisSelection => null;
        public TelemetryRangeSelection? ActiveRearAnalysisSelection => null;
        public void RequestSessionInsights() => SessionInsightsRequestCount++;
        public void PreviewDampingSpeedCutoff(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond) { }
        public void CancelDampingSpeedCutoffPreview() { }
        public Task CommitDampingSpeedCutoffAsync(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond) =>
            Task.CompletedTask;
        public SessionInsightsResult SessionInsights { get; } = new(
            SurfacePresentationState.Ready,
            [new SessionInsightsStep(
                SessionInsightsStepId.Sag,
                "Sag & travel use",
                SessionInsightsSeverity.Watch,
                true,
                [new SessionInsightsMetric("Max travel", "52.0", "%", "Fork", ">= 85 % on hard terrain")],
                [new SessionInsightsFinding(
                    SessionInsightsCategory.TravelUse,
                    SessionInsightsSeverity.Watch,
                    SessionInsightsConfidence.Medium,
                    "Travel use watch",
                    "The fork is not using much travel.",
                    "Try a small pressure experiment and rerun the same section.",
                    [new SessionInsightsEvidence("Max travel", "52.0", "%", "Fork", "Active suspension travel stats")])])],
            [],
            null,
            [new SessionInsightsFinding(
                SessionInsightsCategory.TravelUse,
                SessionInsightsSeverity.Watch,
                SessionInsightsConfidence.Medium,
                "Travel use watch",
                "The fork is not using much travel.",
                "Try a small pressure experiment and rerun the same section.",
                [new SessionInsightsEvidence("Max travel", "52.0", "%", "Fork", "Active suspension travel stats")])]);
    }
}

internal sealed class MountedSessionAnalysisDesktopView(Window host, SessionAnalysisDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public SessionAnalysisDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}

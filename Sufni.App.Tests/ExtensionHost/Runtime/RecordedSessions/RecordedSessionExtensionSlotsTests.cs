using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Plots;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.ExtensionHost.Runtime.RecordedSessions;

public class RecordedSessionExtensionSlotsTests
{
    [Fact]
    public void Collections_AcceptTypedContributions()
    {
        var slots = new RecordedSessionExtensionSlots();
        var action = new TelemetryPlotContextMenuAction(
            "inspect",
            "Inspect",
            new RelayCommand(() => { }));

        slots.SignalToolbarCommands.Add(new RecordedSessionToolbarCommandContribution(
            "extension",
            "toolbar-command",
            Order: 5,
            RecordedSessionToolbarZone.Trailing,
            "Toolbar command",
            Icon: null,
            new RelayCommand(() => { })));
        slots.SignalToolbarViews.Add(new RecordedSessionToolbarViewContribution(
            "extension",
            "toolbar-view",
            Order: 10,
            RecordedSessionToolbarZone.Leading,
            new TestContributionViewModel()));
        var commandContribution = Assert.Single(slots.SignalToolbarCommands);
        Assert.Equal(RecordedSessionToolbarZone.Trailing, commandContribution.Zone);
        slots.SignalPlotContextMenuActions.Add(new RecordedSessionSignalPlotContextMenuContribution(
            "extension",
            "context",
            Order: 20,
            RecordedSessionBuiltInSignalRow.Travel,
            action));
        slots.AnalysisMetrics.Add(new RecordedSessionAnalysisMetricContribution(
            "extension",
            "metric",
            Order: 30,
            RecordedSessionAnalysisMetricTarget.FrontHscPercentage,
            "match 42.00",
            "+3.00",
            RecordedSessionMetricTone.Positive));
        slots.AnalysisTabs.Add(new RecordedSessionAnalysisTabContribution(
            "extension",
            "tab",
            Order: 40,
            "Extension tab",
            RequestedIndex: 3,
            new TestContributionViewModel()));

        var toolbarContribution = Assert.Single(slots.SignalToolbarViews);
        Assert.Equal(RecordedSessionToolbarZone.Leading, toolbarContribution.Zone);
        var contextContribution = Assert.Single(slots.SignalPlotContextMenuActions);
        Assert.Equal("extension", contextContribution.ExtensionId);
        Assert.Equal("context", contextContribution.ContributionId);
        Assert.Equal(20, contextContribution.Order);
        Assert.Equal(RecordedSessionBuiltInSignalRow.Travel, contextContribution.TargetRow);
        Assert.Equal(action, contextContribution.Action);
        var metricContribution = Assert.Single(slots.AnalysisMetrics);
        Assert.Equal(RecordedSessionAnalysisMetricTarget.FrontHscPercentage, metricContribution.TargetMetric);
        Assert.True(metricContribution.HasDeltaValue);
        Assert.True(metricContribution.IsPositiveTone);
        var tabContribution = Assert.Single(slots.AnalysisTabs);
        Assert.Equal("Extension tab", tabContribution.DisplayName);
        Assert.Equal(3, tabContribution.RequestedIndex);
    }

    [Fact]
    public void HostedSignalRows_AcceptNeutralSignalPlotViewModel()
    {
        var slots = new RecordedSessionExtensionSlots();
        var contribution = new RecordedSessionHostedSignalRowContribution(
            "extension",
            "neutral-signal",
            Order: 1,
            RecordedSessionBuiltInSignalRow.Travel,
            RecordedSessionSignalRowTarget.Extension("extension", "neutral-signal"),
            "Matched travel",
            SurfacePresentationState.Ready,
            new RecordedSessionSignalPlotViewModel(
                [], invertValueAxis: true, durationSeconds: 1, emptyMessage: "none", airtimeSpans: []),
            IsInitiallyExpanded: false);

        slots.HostedSignalRows.Add(contribution);

        Assert.IsType<RecordedSessionSignalPlotViewModel>(Assert.Single(slots.HostedSignalRows).ViewModel);
    }

    [Fact]
    public void SubscribeToChanges_NotifiesForEverySlotFamilyUntilDisposed()
    {
        var slots = new RecordedSessionExtensionSlots();
        var notifications = 0;
        using var subscription = slots.SubscribeToChanges(() => notifications++);

        AddOneContributionToEachFamily(slots);

        Assert.Equal(15, notifications);

        subscription.Dispose();
        slots.SignalToolbarViews.Add(CreateToolbarContribution("after-dispose"));

        Assert.Equal(15, notifications);
    }

    [Fact]
    public void Builder_AddFrom_CopiesEverySlotFamily()
    {
        var source = new RecordedSessionExtensionSlots();
        AddOneContributionToEachFamily(source);
        var target = new RecordedSessionExtensionSlots();
        var publisher = new RecordedSessionExtensionSlotPublisher(target, new InlineUiThreadDispatcher());

        publisher.Publish(builder => builder.AddFrom(source));

        Assert.Equal(["toolbar-command"], target.SignalToolbarCommands.Select(contribution => contribution.ContributionId));
        Assert.Equal(["toolbar-view"], target.SignalToolbarViews.Select(contribution => contribution.ContributionId));
        Assert.Equal(["page"], target.Pages.Select(contribution => contribution.ContributionId));
        Assert.Equal(["media"], target.MediaPanes.Select(contribution => contribution.ContributionId));
        Assert.Equal(["map"], target.MapOverlays.Select(contribution => contribution.ContributionId));
        Assert.Equal(["banner"], target.AnalysisBanners.Select(contribution => contribution.ContributionId));
        Assert.Equal(["tab"], target.AnalysisTabs.Select(contribution => contribution.ContributionId));
        Assert.Equal(["overlay"], target.AnalysisOverlays.Select(contribution => contribution.ContributionId));
        Assert.Equal(["metric"], target.AnalysisMetrics.Select(contribution => contribution.ContributionId));
        Assert.Equal(["indicator"], target.SessionListIndicators.Select(contribution => contribution.ContributionId));
        Assert.Equal(["list-action"], target.SessionListActions.Select(contribution => contribution.ContributionId));
        Assert.Equal(["context"], target.SignalPlotContextMenuActions.Select(contribution => contribution.ContributionId));
        Assert.Equal(["row-action"], target.SignalRowHeaderActions.Select(contribution => contribution.ContributionId));
        Assert.Equal(["hosted-row"], target.HostedSignalRows.Select(contribution => contribution.ContributionId));
        Assert.Equal(["range"], target.SignalTimeRangeOverlays.Select(contribution => contribution.ContributionId));
    }

    private static void AddOneContributionToEachFamily(RecordedSessionExtensionSlots slots)
    {
        slots.SignalToolbarCommands.Add(CreateToolbarCommandContribution("toolbar-command"));
        slots.SignalToolbarViews.Add(CreateToolbarContribution("toolbar-view"));
        slots.Pages.Add(new RecordedSessionPageContribution(
            "extension",
            "page",
            Order: 2,
            "Page",
            new TestContributionViewModel(),
            RequestedIndex: 0));
        slots.MediaPanes.Add(new RecordedSessionMediaPaneContribution(
            "extension",
            "media",
            Order: 3,
            new TestContributionViewModel()));
        slots.MapOverlays.Add(new RecordedSessionMapOverlayContribution(
            "extension",
            "map",
            Order: 4,
            Lines: [],
            Points: []));
        slots.AnalysisBanners.Add(new RecordedSessionAnalysisBannerContribution(
            "extension",
            "banner",
            Order: 5,
            new TestContributionViewModel()));
        slots.AnalysisTabs.Add(new RecordedSessionAnalysisTabContribution(
            "extension",
            "tab",
            Order: 6,
            "Extension tab",
            RequestedIndex: 3,
            new TestContributionViewModel()));
        slots.AnalysisOverlays.Add(new RecordedSessionAnalysisOverlayContribution(
            "extension",
            "overlay",
            Order: 7,
            RecordedSessionAnalysisPlotTarget.TravelDistribution(SuspensionType.Front),
            ViewModel: null,
            Overlay: null));
        slots.AnalysisMetrics.Add(new RecordedSessionAnalysisMetricContribution(
            "extension",
            "metric",
            Order: 8,
            RecordedSessionAnalysisMetricTarget.FrontHscPercentage,
            "42.00",
            DeltaValue: null,
            RecordedSessionMetricTone.Default));
        slots.SessionListIndicators.Add(new RecordedSessionListIndicatorContribution(
            "extension",
            "indicator",
            Order: 9,
            new TestContributionViewModel()));
        slots.SessionListActions.Add(new RecordedSessionListActionContribution(
            "extension",
            "list-action",
            Order: 10,
            new TestContributionViewModel()));
        slots.SignalPlotContextMenuActions.Add(new RecordedSessionSignalPlotContextMenuContribution(
            "extension",
            "context",
            Order: 11,
            RecordedSessionBuiltInSignalRow.Travel,
            new TelemetryPlotContextMenuAction("inspect", "Inspect", new RelayCommand(() => { }))));
        slots.SignalRowHeaderActions.Add(new RecordedSessionSignalRowActionContribution(
            "extension",
            "row-action",
            Order: 12,
            RecordedSessionSignalRowTarget.BuiltIn(RecordedSessionBuiltInSignalRow.Travel),
            new SignalRowAction { Id = "row-action" }));
        slots.HostedSignalRows.Add(new RecordedSessionHostedSignalRowContribution(
            "extension",
            "hosted-row",
            Order: 13,
            RecordedSessionBuiltInSignalRow.Travel,
            RecordedSessionSignalRowTarget.Extension("extension", "hosted-row"),
            "Hosted row",
            SurfacePresentationState.Ready,
            new TestContributionViewModel(),
            IsInitiallyExpanded: false));
        slots.SignalTimeRangeOverlays.Add(new RecordedSessionTimeRangeOverlayContribution(
            "extension",
            "range",
            Order: 14,
            RecordedSessionSignalRowTarget.BuiltIn(RecordedSessionBuiltInSignalRow.Travel),
            new RecordedTimeRangeOverlaySetRegistration(
                "range",
                new RecordedTimeRangeOverlaySet(
                    [],
                    new RecordedTimeRangeOverlayStyle(
                        new RecordedTimeRangeOverlayColor(255, 255, 0, 0),
                        new RecordedTimeRangeOverlayColor(255, 0, 0, 255),
                        1.0f)),
                IsVisible: true)));
    }

    private static RecordedSessionToolbarViewContribution CreateToolbarContribution(string contributionId) =>
        new(
            "extension",
            contributionId,
            Order: 1,
            RecordedSessionToolbarZone.Leading,
            new TestContributionViewModel());

    private static RecordedSessionToolbarCommandContribution CreateToolbarCommandContribution(string contributionId) =>
        new(
            "extension",
            contributionId,
            Order: 1,
            RecordedSessionToolbarZone.Leading,
            "Toolbar command",
            Icon: null,
            new RelayCommand(() => { }));

}

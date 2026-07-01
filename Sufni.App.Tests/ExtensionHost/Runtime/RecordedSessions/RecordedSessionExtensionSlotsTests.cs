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
    public void NewSlots_StartEmpty()
    {
        var slots = new RecordedSessionExtensionSlots();

        Assert.Empty(slots.GraphToolbarCommands);
        Assert.Empty(slots.GraphToolbarViews);
        Assert.Empty(slots.Pages);
        Assert.Empty(slots.MediaPanes);
        Assert.Empty(slots.MapOverlays);
        Assert.Empty(slots.StatisticsBanners);
        Assert.Empty(slots.StatisticsTabs);
        Assert.Empty(slots.StatisticsOverlays);
        Assert.Empty(slots.StatisticsMetrics);
        Assert.Empty(slots.SessionListIndicators);
        Assert.Empty(slots.SessionListActions);
        Assert.Empty(slots.PlotContextMenuActions);
        Assert.Empty(slots.PlotRowHeaderActions);
        Assert.Empty(slots.HostedGraphRows);
        Assert.Empty(slots.TimeRangeOverlays);
    }

    [Fact]
    public void Collections_AcceptTypedContributions()
    {
        var slots = new RecordedSessionExtensionSlots();
        var action = new TelemetryPlotContextMenuAction(
            "inspect",
            "Inspect",
            new RelayCommand(() => { }));

        slots.GraphToolbarCommands.Add(new RecordedSessionToolbarCommandContribution(
            "extension",
            "toolbar-command",
            Order: 5,
            RecordedSessionToolbarZone.Trailing,
            "Toolbar command",
            Icon: null,
            new RelayCommand(() => { })));
        slots.GraphToolbarViews.Add(new RecordedSessionToolbarViewContribution(
            "extension",
            "toolbar-view",
            Order: 10,
            RecordedSessionToolbarZone.Leading,
            new TestContributionViewModel()));
        var commandContribution = Assert.Single(slots.GraphToolbarCommands);
        Assert.Equal(RecordedSessionToolbarZone.Trailing, commandContribution.Zone);
        slots.PlotContextMenuActions.Add(new RecordedSessionPlotContextMenuContribution(
            "extension",
            "context",
            Order: 20,
            RecordedSessionBuiltInGraphRow.Travel,
            action));
        slots.StatisticsMetrics.Add(new RecordedSessionStatisticsMetricContribution(
            "extension",
            "metric",
            Order: 30,
            RecordedSessionStatisticsMetricTarget.FrontHscPercentage,
            "match 42.00",
            "+3.00",
            RecordedSessionMetricTone.Positive));
        slots.StatisticsTabs.Add(new RecordedSessionStatisticsTabContribution(
            "extension",
            "tab",
            Order: 40,
            "Extension tab",
            RequestedIndex: 3,
            new TestContributionViewModel()));

        var toolbarContribution = Assert.Single(slots.GraphToolbarViews);
        Assert.Equal(RecordedSessionToolbarZone.Leading, toolbarContribution.Zone);
        var contextContribution = Assert.Single(slots.PlotContextMenuActions);
        Assert.Equal("extension", contextContribution.ExtensionId);
        Assert.Equal("context", contextContribution.ContributionId);
        Assert.Equal(20, contextContribution.Order);
        Assert.Equal(RecordedSessionBuiltInGraphRow.Travel, contextContribution.TargetRow);
        Assert.Equal(action, contextContribution.Action);
        var metricContribution = Assert.Single(slots.StatisticsMetrics);
        Assert.Equal(RecordedSessionStatisticsMetricTarget.FrontHscPercentage, metricContribution.TargetMetric);
        Assert.True(metricContribution.HasDeltaValue);
        Assert.True(metricContribution.IsPositiveTone);
        var tabContribution = Assert.Single(slots.StatisticsTabs);
        Assert.Equal("Extension tab", tabContribution.DisplayName);
        Assert.Equal(3, tabContribution.RequestedIndex);
    }

    [Fact]
    public void HostedGraphRows_AcceptNeutralSeriesGraphViewModel()
    {
        var slots = new RecordedSessionExtensionSlots();
        var contribution = new RecordedSessionHostedGraphRowContribution(
            "extension",
            "neutral-series",
            Order: 1,
            RecordedSessionBuiltInGraphRow.Travel,
            RecordedSessionGraphRowTarget.Extension("extension", "neutral-series"),
            "Matched travel",
            SurfacePresentationState.Ready,
            new RecordedSessionSeriesGraphViewModel(
                [], invertValueAxis: true, durationSeconds: 1, emptyMessage: "none", airtimeSpans: []),
            IsInitiallyExpanded: false);

        slots.HostedGraphRows.Add(contribution);

        Assert.IsType<RecordedSessionSeriesGraphViewModel>(Assert.Single(slots.HostedGraphRows).ViewModel);
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
        slots.GraphToolbarViews.Add(CreateToolbarContribution("after-dispose"));

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

        Assert.Equal(["toolbar-command"], target.GraphToolbarCommands.Select(contribution => contribution.ContributionId));
        Assert.Equal(["toolbar-view"], target.GraphToolbarViews.Select(contribution => contribution.ContributionId));
        Assert.Equal(["page"], target.Pages.Select(contribution => contribution.ContributionId));
        Assert.Equal(["media"], target.MediaPanes.Select(contribution => contribution.ContributionId));
        Assert.Equal(["map"], target.MapOverlays.Select(contribution => contribution.ContributionId));
        Assert.Equal(["banner"], target.StatisticsBanners.Select(contribution => contribution.ContributionId));
        Assert.Equal(["tab"], target.StatisticsTabs.Select(contribution => contribution.ContributionId));
        Assert.Equal(["overlay"], target.StatisticsOverlays.Select(contribution => contribution.ContributionId));
        Assert.Equal(["metric"], target.StatisticsMetrics.Select(contribution => contribution.ContributionId));
        Assert.Equal(["indicator"], target.SessionListIndicators.Select(contribution => contribution.ContributionId));
        Assert.Equal(["list-action"], target.SessionListActions.Select(contribution => contribution.ContributionId));
        Assert.Equal(["context"], target.PlotContextMenuActions.Select(contribution => contribution.ContributionId));
        Assert.Equal(["row-action"], target.PlotRowHeaderActions.Select(contribution => contribution.ContributionId));
        Assert.Equal(["hosted-row"], target.HostedGraphRows.Select(contribution => contribution.ContributionId));
        Assert.Equal(["range"], target.TimeRangeOverlays.Select(contribution => contribution.ContributionId));
    }

    private static void AddOneContributionToEachFamily(RecordedSessionExtensionSlots slots)
    {
        slots.GraphToolbarCommands.Add(CreateToolbarCommandContribution("toolbar-command"));
        slots.GraphToolbarViews.Add(CreateToolbarContribution("toolbar-view"));
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
        slots.StatisticsBanners.Add(new RecordedSessionStatisticsBannerContribution(
            "extension",
            "banner",
            Order: 5,
            new TestContributionViewModel()));
        slots.StatisticsTabs.Add(new RecordedSessionStatisticsTabContribution(
            "extension",
            "tab",
            Order: 6,
            "Extension tab",
            RequestedIndex: 3,
            new TestContributionViewModel()));
        slots.StatisticsOverlays.Add(new RecordedSessionStatisticsOverlayContribution(
            "extension",
            "overlay",
            Order: 7,
            RecordedSessionStatisticsPlotTarget.TravelHistogram(SuspensionType.Front),
            ViewModel: null,
            Overlay: null));
        slots.StatisticsMetrics.Add(new RecordedSessionStatisticsMetricContribution(
            "extension",
            "metric",
            Order: 8,
            RecordedSessionStatisticsMetricTarget.FrontHscPercentage,
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
        slots.PlotContextMenuActions.Add(new RecordedSessionPlotContextMenuContribution(
            "extension",
            "context",
            Order: 11,
            RecordedSessionBuiltInGraphRow.Travel,
            new TelemetryPlotContextMenuAction("inspect", "Inspect", new RelayCommand(() => { }))));
        slots.PlotRowHeaderActions.Add(new RecordedSessionPlotRowActionContribution(
            "extension",
            "row-action",
            Order: 12,
            RecordedSessionGraphRowTarget.BuiltIn(RecordedSessionBuiltInGraphRow.Travel),
            new TelemetryPlotRowAction { Id = "row-action" }));
        slots.HostedGraphRows.Add(new RecordedSessionHostedGraphRowContribution(
            "extension",
            "hosted-row",
            Order: 13,
            RecordedSessionBuiltInGraphRow.Travel,
            RecordedSessionGraphRowTarget.Extension("extension", "hosted-row"),
            "Hosted row",
            SurfacePresentationState.Ready,
            new TestContributionViewModel(),
            IsInitiallyExpanded: false));
        slots.TimeRangeOverlays.Add(new RecordedSessionTimeRangeOverlayContribution(
            "extension",
            "range",
            Order: 14,
            RecordedSessionGraphRowTarget.BuiltIn(RecordedSessionBuiltInGraphRow.Travel),
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

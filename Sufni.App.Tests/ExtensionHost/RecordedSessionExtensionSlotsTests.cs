using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.Plots;
using Sufni.App.Presentation;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Controls;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Plots;
using Sufni.App.ExtensionHost.Presentation;
using Sufni.App.ExtensionHost.ViewModels.Editors;
using Sufni.App.ExtensionHost.Views.Controls;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.ExtensionHost;

public class RecordedSessionExtensionSlotsTests
{
    [Fact]
    public void NewSlots_StartEmpty()
    {
        var slots = new RecordedSessionExtensionSlots();

        Assert.Empty(slots.GraphToolbarActions);
        Assert.Empty(slots.Pages);
        Assert.Empty(slots.MediaPanes);
        Assert.Empty(slots.MapOverlays);
        Assert.Empty(slots.StatisticsBanners);
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

        slots.GraphToolbarActions.Add(new RecordedSessionToolbarContribution(
            "extension",
            "toolbar",
            Order: 10,
            RecordedSessionToolbarZone.Leading,
            new TestContributionViewModel()));
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

        var toolbarContribution = Assert.Single(slots.GraphToolbarActions);
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
    }

    [Fact]
    public void SubscribeToChanges_NotifiesForEverySlotFamilyUntilDisposed()
    {
        var slots = new RecordedSessionExtensionSlots();
        var notifications = 0;
        using var subscription = slots.SubscribeToChanges(() => notifications++);

        AddOneContributionToEachFamily(slots);

        Assert.Equal(13, notifications);

        subscription.Dispose();
        slots.GraphToolbarActions.Add(CreateToolbarContribution("after-dispose"));

        Assert.Equal(13, notifications);
    }

    [Fact]
    public void Builder_AddFrom_CopiesEverySlotFamily()
    {
        var source = new RecordedSessionExtensionSlots();
        AddOneContributionToEachFamily(source);
        var target = new RecordedSessionExtensionSlots();
        var publisher = new RecordedSessionExtensionSlotPublisher(target, new InlineUiThreadDispatcher());

        publisher.Publish(builder => builder.AddFrom(source));

        Assert.Equal(["toolbar"], target.GraphToolbarActions.Select(contribution => contribution.ContributionId));
        Assert.Equal(["page"], target.Pages.Select(contribution => contribution.ContributionId));
        Assert.Equal(["media"], target.MediaPanes.Select(contribution => contribution.ContributionId));
        Assert.Equal(["map"], target.MapOverlays.Select(contribution => contribution.ContributionId));
        Assert.Equal(["banner"], target.StatisticsBanners.Select(contribution => contribution.ContributionId));
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
        slots.GraphToolbarActions.Add(CreateToolbarContribution("toolbar"));
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
        slots.StatisticsOverlays.Add(new RecordedSessionStatisticsOverlayContribution(
            "extension",
            "overlay",
            Order: 6,
            RecordedSessionStatisticsPlotTarget.TravelHistogram(SuspensionType.Front),
            ViewModel: null,
            Overlay: null));
        slots.StatisticsMetrics.Add(new RecordedSessionStatisticsMetricContribution(
            "extension",
            "metric",
            Order: 7,
            RecordedSessionStatisticsMetricTarget.FrontHscPercentage,
            "42.00",
            DeltaValue: null,
            RecordedSessionMetricTone.Default));
        slots.SessionListIndicators.Add(new RecordedSessionListIndicatorContribution(
            "extension",
            "indicator",
            Order: 8,
            new TestContributionViewModel()));
        slots.SessionListActions.Add(new RecordedSessionListActionContribution(
            "extension",
            "list-action",
            Order: 9,
            new TestContributionViewModel()));
        slots.PlotContextMenuActions.Add(new RecordedSessionPlotContextMenuContribution(
            "extension",
            "context",
            Order: 10,
            RecordedSessionBuiltInGraphRow.Travel,
            new TelemetryPlotContextMenuAction("inspect", "Inspect", new RelayCommand(() => { }))));
        slots.PlotRowHeaderActions.Add(new RecordedSessionPlotRowActionContribution(
            "extension",
            "row-action",
            Order: 11,
            RecordedSessionGraphRowTarget.BuiltIn(RecordedSessionBuiltInGraphRow.Travel),
            new TelemetryPlotRowAction { Id = "row-action" }));
        slots.HostedGraphRows.Add(new RecordedSessionHostedGraphRowContribution(
            "extension",
            "hosted-row",
            Order: 12,
            RecordedSessionBuiltInGraphRow.Travel,
            RecordedSessionGraphRowTarget.Extension("extension", "hosted-row"),
            "Hosted row",
            SurfacePresentationState.Ready,
            new TestContributionViewModel(),
            IsInitiallyExpanded: false));
        slots.TimeRangeOverlays.Add(new RecordedSessionTimeRangeOverlayContribution(
            "extension",
            "range",
            Order: 13,
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

    private static RecordedSessionToolbarContribution CreateToolbarContribution(string contributionId) =>
        new(
            "extension",
            contributionId,
            Order: 1,
            RecordedSessionToolbarZone.Leading,
            new TestContributionViewModel());

}

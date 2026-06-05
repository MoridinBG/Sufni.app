using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.ViewModels.Editors;

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
            new object()));
        slots.PlotContextMenuActions.Add(new RecordedSessionPlotContextMenuContribution(
            "extension",
            "context",
            Order: 20,
            RowId: "travel",
            action));
        slots.StatisticsMetrics.Add(new RecordedSessionStatisticsMetricContribution(
            "extension",
            "metric",
            Order: 30,
            RecordedSessionStatisticsMetricIds.FrontHscPercentage,
            "match 42.00",
            "+3.00",
            RecordedSessionMetricTone.Positive));

        var toolbarContribution = Assert.Single(slots.GraphToolbarActions);
        Assert.Equal(RecordedSessionToolbarZone.Leading, toolbarContribution.Zone);
        var contextContribution = Assert.Single(slots.PlotContextMenuActions);
        Assert.Equal("extension", contextContribution.ExtensionId);
        Assert.Equal("context", contextContribution.ContributionId);
        Assert.Equal(20, contextContribution.Order);
        Assert.Equal("travel", contextContribution.RowId);
        Assert.Equal(action, contextContribution.Action);
        var metricContribution = Assert.Single(slots.StatisticsMetrics);
        Assert.Equal(RecordedSessionStatisticsMetricIds.FrontHscPercentage, metricContribution.TargetMetricId);
        Assert.True(metricContribution.HasDeltaValue);
        Assert.True(metricContribution.IsPositiveTone);
    }
}

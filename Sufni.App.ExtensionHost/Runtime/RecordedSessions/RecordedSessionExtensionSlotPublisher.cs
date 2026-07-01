using System;
using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.ExtensionHost.Runtime.RecordedSessions;

public sealed class RecordedSessionExtensionSlotBuilder
{
    public List<RecordedSessionToolbarCommandContribution> GraphToolbarCommands { get; } = [];
    public List<RecordedSessionToolbarViewContribution> GraphToolbarViews { get; } = [];
    public List<RecordedSessionPageContribution> Pages { get; } = [];
    public List<RecordedSessionMediaPaneContribution> MediaPanes { get; } = [];
    public List<RecordedSessionMapOverlayContribution> MapOverlays { get; } = [];
    public List<RecordedSessionStatisticsBannerContribution> StatisticsBanners { get; } = [];
    public List<RecordedSessionStatisticsTabContribution> StatisticsTabs { get; } = [];
    public List<RecordedSessionStatisticsOverlayContribution> StatisticsOverlays { get; } = [];
    public List<RecordedSessionStatisticsMetricContribution> StatisticsMetrics { get; } = [];
    public List<RecordedSessionListIndicatorContribution> SessionListIndicators { get; } = [];
    public List<RecordedSessionListActionContribution> SessionListActions { get; } = [];
    public List<RecordedSessionPlotContextMenuContribution> PlotContextMenuActions { get; } = [];
    public List<RecordedSessionPlotRowActionContribution> PlotRowHeaderActions { get; } = [];
    public List<RecordedSessionHostedGraphRowContribution> HostedGraphRows { get; } = [];
    public List<RecordedSessionTimeRangeOverlayContribution> TimeRangeOverlays { get; } = [];

    public void AddFrom(RecordedSessionExtensionSlots slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        GraphToolbarCommands.AddRange(slots.GraphToolbarCommands);
        GraphToolbarViews.AddRange(slots.GraphToolbarViews);
        Pages.AddRange(slots.Pages);
        MediaPanes.AddRange(slots.MediaPanes);
        MapOverlays.AddRange(slots.MapOverlays);
        StatisticsBanners.AddRange(slots.StatisticsBanners);
        StatisticsTabs.AddRange(slots.StatisticsTabs);
        StatisticsOverlays.AddRange(slots.StatisticsOverlays);
        StatisticsMetrics.AddRange(slots.StatisticsMetrics);
        SessionListIndicators.AddRange(slots.SessionListIndicators);
        SessionListActions.AddRange(slots.SessionListActions);
        PlotContextMenuActions.AddRange(slots.PlotContextMenuActions);
        PlotRowHeaderActions.AddRange(slots.PlotRowHeaderActions);
        HostedGraphRows.AddRange(slots.HostedGraphRows);
        TimeRangeOverlays.AddRange(slots.TimeRangeOverlays);
    }

    internal void PublishTo(RecordedSessionExtensionSlots slots)
    {
        slots.GraphToolbarCommands.ReplaceWith(GraphToolbarCommands);
        slots.GraphToolbarViews.ReplaceWith(GraphToolbarViews);
        slots.Pages.ReplaceWith(Pages);
        slots.MediaPanes.ReplaceWith(MediaPanes);
        slots.MapOverlays.ReplaceWith(MapOverlays);
        slots.StatisticsBanners.ReplaceWith(StatisticsBanners);
        slots.StatisticsTabs.ReplaceWith(StatisticsTabs);
        slots.StatisticsOverlays.ReplaceWith(StatisticsOverlays);
        slots.StatisticsMetrics.ReplaceWith(StatisticsMetrics);
        slots.SessionListIndicators.ReplaceWith(SessionListIndicators);
        slots.SessionListActions.ReplaceWith(SessionListActions);
        slots.PlotContextMenuActions.ReplaceWith(PlotContextMenuActions);
        slots.PlotRowHeaderActions.ReplaceWith(PlotRowHeaderActions);
        slots.HostedGraphRows.ReplaceWith(HostedGraphRows);
        slots.TimeRangeOverlays.ReplaceWith(TimeRangeOverlays);
    }
}

public sealed class RecordedSessionExtensionSlotPublisher
{
    private readonly RecordedSessionExtensionSlots slots;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly System.Threading.Lock gate = new();
    private Action<RecordedSessionExtensionSlotBuilder>? pendingBuild;
    private bool publishQueued;

    public RecordedSessionExtensionSlotPublisher(
        RecordedSessionExtensionSlots slots,
        IUiThreadDispatcher uiThreadDispatcher)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(uiThreadDispatcher);

        this.slots = slots;
        this.uiThreadDispatcher = uiThreadDispatcher;
    }

    public void Publish(Action<RecordedSessionExtensionSlotBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        var builder = new RecordedSessionExtensionSlotBuilder();
        build(builder);
        builder.PublishTo(slots);
    }

    public void RequestPublish(Action<RecordedSessionExtensionSlotBuilder> build)
    {
        ArgumentNullException.ThrowIfNull(build);

        if (uiThreadDispatcher.CheckAccess())
        {
            lock (gate)
            {
                pendingBuild = null;
                publishQueued = false;
            }

            Publish(build);
            return;
        }

        var shouldQueue = false;
        lock (gate)
        {
            pendingBuild = build;
            if (!publishQueued)
            {
                publishQueued = true;
                shouldQueue = true;
            }
        }

        if (shouldQueue)
        {
            uiThreadDispatcher.Post(PublishPending);
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            pendingBuild = null;
            publishQueued = false;
        }

        Publish(_ => { });
    }

    private void PublishPending()
    {
        Action<RecordedSessionExtensionSlotBuilder>? build;
        lock (gate)
        {
            publishQueued = false;
            build = pendingBuild;
            pendingBuild = null;
        }

        if (build is null)
        {
            return;
        }

        Publish(build);
    }
}

using System;
using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.ExtensionHost.Runtime.RecordedSessions;

public sealed class RecordedSessionExtensionSlotBuilder
{
    public List<RecordedSessionToolbarCommandContribution> SignalToolbarCommands { get; } = [];
    public List<RecordedSessionToolbarViewContribution> SignalToolbarViews { get; } = [];
    public List<RecordedSessionPageContribution> Pages { get; } = [];
    public List<RecordedSessionMediaPaneContribution> MediaPanes { get; } = [];
    public List<RecordedSessionMapOverlayContribution> MapOverlays { get; } = [];
    public List<RecordedSessionAnalysisBannerContribution> AnalysisBanners { get; } = [];
    public List<RecordedSessionAnalysisTabContribution> AnalysisTabs { get; } = [];
    public List<RecordedSessionAnalysisOverlayContribution> AnalysisOverlays { get; } = [];
    public List<RecordedSessionAnalysisMetricContribution> AnalysisMetrics { get; } = [];
    public List<RecordedSessionListIndicatorContribution> SessionListIndicators { get; } = [];
    public List<RecordedSessionListActionContribution> SessionListActions { get; } = [];
    public List<RecordedSessionSignalPlotContextMenuContribution> SignalPlotContextMenuActions { get; } = [];
    public List<RecordedSessionSignalRowActionContribution> SignalRowHeaderActions { get; } = [];
    public List<RecordedSessionHostedSignalRowContribution> HostedSignalRows { get; } = [];
    public List<RecordedSessionTimeRangeOverlayContribution> SignalTimeRangeOverlays { get; } = [];

    public void AddFrom(RecordedSessionExtensionSlots slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        SignalToolbarCommands.AddRange(slots.SignalToolbarCommands);
        SignalToolbarViews.AddRange(slots.SignalToolbarViews);
        Pages.AddRange(slots.Pages);
        MediaPanes.AddRange(slots.MediaPanes);
        MapOverlays.AddRange(slots.MapOverlays);
        AnalysisBanners.AddRange(slots.AnalysisBanners);
        AnalysisTabs.AddRange(slots.AnalysisTabs);
        AnalysisOverlays.AddRange(slots.AnalysisOverlays);
        AnalysisMetrics.AddRange(slots.AnalysisMetrics);
        SessionListIndicators.AddRange(slots.SessionListIndicators);
        SessionListActions.AddRange(slots.SessionListActions);
        SignalPlotContextMenuActions.AddRange(slots.SignalPlotContextMenuActions);
        SignalRowHeaderActions.AddRange(slots.SignalRowHeaderActions);
        HostedSignalRows.AddRange(slots.HostedSignalRows);
        SignalTimeRangeOverlays.AddRange(slots.SignalTimeRangeOverlays);
    }

    internal void PublishTo(RecordedSessionExtensionSlots slots)
    {
        slots.SignalToolbarCommands.ReplaceWith(SignalToolbarCommands);
        slots.SignalToolbarViews.ReplaceWith(SignalToolbarViews);
        slots.Pages.ReplaceWith(Pages);
        slots.MediaPanes.ReplaceWith(MediaPanes);
        slots.MapOverlays.ReplaceWith(MapOverlays);
        slots.AnalysisBanners.ReplaceWith(AnalysisBanners);
        slots.AnalysisTabs.ReplaceWith(AnalysisTabs);
        slots.AnalysisOverlays.ReplaceWith(AnalysisOverlays);
        slots.AnalysisMetrics.ReplaceWith(AnalysisMetrics);
        slots.SessionListIndicators.ReplaceWith(SessionListIndicators);
        slots.SessionListActions.ReplaceWith(SessionListActions);
        slots.SignalPlotContextMenuActions.ReplaceWith(SignalPlotContextMenuActions);
        slots.SignalRowHeaderActions.ReplaceWith(SignalRowHeaderActions);
        slots.HostedSignalRows.ReplaceWith(HostedSignalRows);
        slots.SignalTimeRangeOverlays.ReplaceWith(SignalTimeRangeOverlays);
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

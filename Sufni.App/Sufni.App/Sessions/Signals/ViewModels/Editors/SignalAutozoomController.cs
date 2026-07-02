using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
namespace Sufni.App.Sessions.Signals.ViewModels.Editors;

internal sealed class SignalAutozoomController
{
    private readonly SessionTimelineLinkViewModel timeline;

    public SignalAutozoomController(SessionTimelineLinkViewModel timeline)
    {
        this.timeline = timeline;
        var autozoomCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            AutozoomPlot,
            CanAutozoomPlot);
        ActionsBySignalRowId = CreateActionsBySignalRowId(autozoomCommand);
    }

    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> ActionsBySignalRowId { get; }

    private static IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> CreateActionsBySignalRowId(
        IRelayCommand<TelemetryPlotContextMenuContext?> autozoomCommand)
    {
        var autozoom = new TelemetryPlotContextMenuAction("autozoom", "Autozoom", autozoomCommand);
        return new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>
        {
            [SignalRowIds.Travel] = [autozoom],
            [SignalRowIds.Velocity] = [autozoom],
            [SignalRowIds.Imu] = [autozoom],
            [SignalRowIds.PitchRoll] = [autozoom],
            [SignalRowIds.Speed] = [autozoom],
            [SignalRowIds.Elevation] = [autozoom],
        };
    }

    private static bool CanAutozoomPlot(TelemetryPlotContextMenuContext? context)
    {
        return context is not null &&
               double.IsFinite(context.DurationSeconds) &&
               context.DurationSeconds > 0 &&
               double.IsFinite(context.ClickSeconds);
    }

    private void AutozoomPlot(TelemetryPlotContextMenuContext? context)
    {
        if (context is null || !CanAutozoomPlot(context))
        {
            return;
        }

        var duration = context.DurationSeconds;
        var (startSeconds, endSeconds) = context.IsClickInsideAnalysisRange && context.AnalysisRange is { } range
            ? CreateSelectionAutozoomRange(range, duration)
            : (0d, duration);

        timeline.SetVisibleRange(startSeconds / duration, endSeconds / duration, this);
    }

    private static (double StartSeconds, double EndSeconds) CreateSelectionAutozoomRange(
        TelemetryTimeRange range,
        double durationSeconds)
    {
        const double paddingFraction = 0.05;
        const double minimumSpanFraction = 0.01;

        var selectionStart = Math.Clamp(range.StartSeconds, 0, durationSeconds);
        var selectionEnd = Math.Clamp(range.EndSeconds, 0, durationSeconds);
        var selectionSpan = Math.Max(0, selectionEnd - selectionStart);
        var padding = selectionSpan * paddingFraction;
        var start = Math.Clamp(selectionStart - padding, 0, durationSeconds);
        var end = Math.Clamp(selectionEnd + padding, 0, durationSeconds);
        var minimumSpan = durationSeconds * minimumSpanFraction;

        if (end - start < minimumSpan)
        {
            var center = (start + end) / 2.0;
            start = center - minimumSpan / 2.0;
            end = center + minimumSpan / 2.0;
        }

        if (start < 0)
        {
            end -= start;
            start = 0;
        }

        if (end > durationSeconds)
        {
            start -= end - durationSeconds;
            end = durationSeconds;
        }

        start = Math.Clamp(start, 0, durationSeconds);
        end = Math.Clamp(end, 0, durationSeconds);
        return (start, end);
    }
}

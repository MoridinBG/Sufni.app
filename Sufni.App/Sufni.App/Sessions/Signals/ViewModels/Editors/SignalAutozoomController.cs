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
        var zoomSelectionCommand = new RelayCommand<TelemetryPlotContextMenuContext?>(
            ZoomSelection,
            CanZoomSelection);
        ActionsBySignalRowId = CreateActionsBySignalRowId(zoomSelectionCommand);
    }

    public IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> ActionsBySignalRowId { get; }

    private static IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>> CreateActionsBySignalRowId(
        IRelayCommand<TelemetryPlotContextMenuContext?> zoomSelectionCommand)
    {
        var zoomSelection = new TelemetryPlotContextMenuAction("zoom-selection", "Zoom selection", zoomSelectionCommand);
        return new Dictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>
        {
            [SignalRowIds.Travel] = [zoomSelection],
            [SignalRowIds.Velocity] = [zoomSelection],
            [SignalRowIds.Imu] = [zoomSelection],
            [SignalRowIds.PitchRoll] = [zoomSelection],
            [SignalRowIds.Speed] = [zoomSelection],
            [SignalRowIds.Elevation] = [zoomSelection],
        };
    }

    private static bool CanZoomSelection(TelemetryPlotContextMenuContext? context)
    {
        return context is not null &&
               double.IsFinite(context.DurationSeconds) &&
               context.DurationSeconds > 0 &&
               context.AnalysisRange is not null;
    }

    private void ZoomSelection(TelemetryPlotContextMenuContext? context)
    {
        if (context is null || !CanZoomSelection(context) || context.AnalysisRange is not { } range)
        {
            return;
        }

        var duration = context.DurationSeconds;
        var (startSeconds, endSeconds) = CreateSelectionAutozoomRange(range, duration);

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

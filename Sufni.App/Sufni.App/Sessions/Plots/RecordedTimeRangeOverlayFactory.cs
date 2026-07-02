using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Plots;

using Sufni.App.Theming;
using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Sessions.Plots;

public static class RecordedTimeRangeOverlayFactory
{
    public const double AirtimeLabelFontSize = 13;
    private static readonly RecordedTimeRangeOverlayColor Transparent = new(0, 0, 0, 0);

    public static RecordedTimeRangeOverlaySetRegistration CreateAirtimeRegistration(
        IEnumerable<Airtime> airtimes,
        SufniPlotTheme plotTheme,
        bool isVisible = false,
        bool includeLabels = false,
        double labelY = 0)
    {
        return CreateAirtimeRegistration(
            airtimes.Select(airtime => (airtime.Start, airtime.End)),
            plotTheme,
            isVisible,
            includeLabels,
            labelY);
    }

    public static RecordedTimeRangeOverlaySetRegistration CreateAirtimeRegistration(
        IEnumerable<(double StartSeconds, double EndSeconds)> airtimes,
        SufniPlotTheme plotTheme,
        bool isVisible = false,
        bool includeLabels = false,
        double labelY = 0)
    {
        var ranges = airtimes
            .Select(range => new RecordedTimeRangeOverlay(
                range.StartSeconds,
                range.EndSeconds,
                includeLabels ? $"{range.EndSeconds - range.StartSeconds:0.##}s air" : null))
            .ToArray();
        var labelOptions = includeLabels
            ? new RecordedTimeRangeOverlayLabelOptions(labelY, AirtimeLabelFontSize, CullCollisions: true)
            : null;

        return new RecordedTimeRangeOverlaySetRegistration(
            RecordedTimeRangeOverlayIds.Airtime,
            new RecordedTimeRangeOverlaySet(
                ranges,
                new RecordedTimeRangeOverlayStyle(
                    plotTheme.Marker.AirtimeFill.ToRecordedTimeRangeOverlayColor(),
                    plotTheme.Marker.AirtimeOutline.ToRecordedTimeRangeOverlayColor(),
                    1.0f),
                labelOptions),
            isVisible);
    }

    public static RecordedTimeRangeOverlaySetRegistration CreateAnalysisRangeRegistration(
        TelemetryTimeRange range,
        SufniPlotTheme plotTheme)
    {
        return new RecordedTimeRangeOverlaySetRegistration(
            RecordedTimeRangeOverlayIds.AnalysisRange,
            new RecordedTimeRangeOverlaySet(
                [new RecordedTimeRangeOverlay(range.StartSeconds, range.EndSeconds)],
                new RecordedTimeRangeOverlayStyle(
                    plotTheme.AnalysisRange.SelectedFill.ToRecordedTimeRangeOverlayColor(),
                    Transparent,
                    0)),
            IsVisible: true);
    }

    public static RecordedTimeRangeOverlaySetRegistration CreateAnalysisSelectionRegistration(
        IEnumerable<TelemetryHighlightRange> ranges,
        SufniPlotTheme plotTheme,
        bool isVisible = false)
    {
        return new RecordedTimeRangeOverlaySetRegistration(
            RecordedTimeRangeOverlayIds.AnalysisSelection,
            new RecordedTimeRangeOverlaySet(
                ranges
                    .Select(range => new RecordedTimeRangeOverlay(
                        range.StartSeconds,
                        range.EndSeconds,
                        Style: CreateAnalysisSelectionStyle(range.SuspensionType, plotTheme)))
                    .ToArray(),
                new RecordedTimeRangeOverlayStyle(
                    plotTheme.Marker.DampingSelectionFill.ToRecordedTimeRangeOverlayColor(),
                    plotTheme.Marker.DampingSelectionOutline.ToRecordedTimeRangeOverlayColor(),
                    1.0f)),
            isVisible);
    }

    private static RecordedTimeRangeOverlayStyle? CreateAnalysisSelectionStyle(
        SuspensionType? suspensionType,
        SufniPlotTheme plotTheme)
    {
        return suspensionType switch
        {
            SuspensionType.Front => new RecordedTimeRangeOverlayStyle(
                plotTheme.Marker.AnalysisSelectionFrontFill.ToRecordedTimeRangeOverlayColor(),
                plotTheme.Marker.AnalysisSelectionFrontOutline.ToRecordedTimeRangeOverlayColor(),
                1.0f),
            SuspensionType.Rear => new RecordedTimeRangeOverlayStyle(
                plotTheme.Marker.AnalysisSelectionRearFill.ToRecordedTimeRangeOverlayColor(),
                plotTheme.Marker.AnalysisSelectionRearOutline.ToRecordedTimeRangeOverlayColor(),
                1.0f),
            _ => null,
        };
    }

    public static RecordedTimeRangeOverlaySetRegistration CreatePreviewRangeRegistration(
        double startSeconds,
        double endSeconds,
        SufniPlotTheme plotTheme)
    {
        var start = Math.Min(startSeconds, endSeconds);
        var end = Math.Max(startSeconds, endSeconds);
        return new RecordedTimeRangeOverlaySetRegistration(
            RecordedTimeRangeOverlayIds.PreviewRange,
            new RecordedTimeRangeOverlaySet(
                [new RecordedTimeRangeOverlay(start, end)],
                new RecordedTimeRangeOverlayStyle(
                    plotTheme.AnalysisRange.PreviewFill.ToRecordedTimeRangeOverlayColor(),
                    Transparent,
                    0)),
            IsVisible: true);
    }
}

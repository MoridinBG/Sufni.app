using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using Sufni.App.Theming;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public static class RecordedTimeRangeOverlayIds
{
    public const string AnalysisRange = "analysis_range";
    public const string PreviewRange = "preview_range";
    public const string Airtime = "airtime";
}

public sealed record RecordedTimeRangeOverlayStyle(
    Color FillColor,
    Color OutlineColor,
    float OutlineWidth);

public sealed record RecordedTimeRangeOverlayLabelOptions(
    double Y,
    double FontSize,
    bool CullCollisions);

public sealed record RecordedTimeRangeOverlay(
    double StartSeconds,
    double EndSeconds,
    string? Label = null);

public sealed record RecordedTimeRangeOverlaySet(
    IReadOnlyList<RecordedTimeRangeOverlay> Ranges,
    RecordedTimeRangeOverlayStyle Style,
    RecordedTimeRangeOverlayLabelOptions? LabelOptions = null);

public sealed record RecordedTimeRangeOverlaySetRegistration(
    string Id,
    RecordedTimeRangeOverlaySet Set,
    bool IsVisible);

public static class RecordedTimeRangeOverlayFactory
{
    public const double AirtimeLabelFontSize = 13;

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
                    plotTheme.Marker.AirtimeFill.ToScottPlotColor(),
                    plotTheme.Marker.AirtimeOutline.ToScottPlotColor(),
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
                    plotTheme.AnalysisRange.SelectedFill.ToScottPlotColor(),
                    Colors.Transparent,
                    0)),
            IsVisible: true);
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
                    plotTheme.AnalysisRange.PreviewFill.ToScottPlotColor(),
                    Colors.Transparent,
                    0)),
            IsVisible: true);
    }
}

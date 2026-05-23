using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;
using Sufni.App.Theming;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class TravelPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
{
    private const double AirtimeLabelYInset = 10;
    private const double AirtimeLabelFontSize = 13;
    private const double AirtimeLabelAverageCharacterWidthFactor = 0.58;

    private readonly List<AirtimeLabel> airtimeLabels = [];

    public override void Clear()
    {
        airtimeLabels.Clear();
        base.Clear();
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        var maxTravel = Math.Max(
            telemetryData.Front.Present ? telemetryData.Front.MaxTravel!.Value : 0,
            telemetryData.Rear.Present ? telemetryData.Rear.MaxTravel!.Value : 0);
        var series = new List<RecordedTimeSeries>();

        if (telemetryData.Front.Present)
        {
            series.Add(new RecordedTimeSeries(
                "Front",
                "mm",
                FrontColor,
                new SampledValues(telemetryData.Front.Travel, telemetryData.Metadata.SampleRate),
                "0.#",
                SourceKey: TelemetrySourceKeys.Front));
        }

        if (telemetryData.Rear.Present)
        {
            series.Add(new RecordedTimeSeries(
                "Rear",
                "mm",
                RearColor,
                new SampledValues(telemetryData.Rear.Travel, telemetryData.Metadata.SampleRate),
                "0.#",
                SourceKey: TelemetrySourceKeys.Rear));
        }

        LoadTimeSeries(new RecordedTimeSeriesData(
            "Travel (mm)",
            "No travel data",
            telemetryData.Metadata.Duration,
            series,
            new RecordedTimeSeriesValueRange(maxTravel, 0),
            telemetryData,
            ShowLegendWhenSingleSource: true,
            EnableInteractiveLegend: true,
            InteractiveLegendRowId: TelemetryGraphRowIds.Travel));
    }

    protected override void AddTimeSeriesOverlays(RecordedTimeSeriesData data)
    {
        airtimeLabels.Clear();

        if (data.MarkerSource is not { } telemetryData)
        {
            return;
        }

        var maxTravel = data.ValueRange?.Minimum ?? 0;
        foreach (var airtime in telemetryData.Airtimes)
        {
            var span = Plot.Add.HorizontalSpan(airtime.Start, airtime.End);
            span.FillColor = PlotTheme.Marker.AirtimeFill.ToScottPlotColor();
            span.LineStyle.Color = PlotTheme.Marker.AirtimeOutline.ToScottPlotColor();
            span.LineStyle.Width = 1.0f;

            var timeSpan = airtime.End - airtime.Start;
            var labelContent = $"{timeSpan:0.##}s air";
            var labelCenter = airtime.Start + timeSpan / 2;
            var label = AddLabel(labelContent, labelCenter, maxTravel - AirtimeLabelYInset,
                0, 0, Alignment.LowerCenter);
            airtimeLabels.Add(new AirtimeLabel(
                label,
                labelCenter,
                timeSpan,
                EstimateLabelWidthPixels(labelContent)));
        }
    }

    public void UpdateAirtimeLabelVisibility(
        double visibleMinimumSeconds,
        double visibleMaximumSeconds,
        double dataAreaWidthPixels)
    {
        if (airtimeLabels.Count == 0 || dataAreaWidthPixels <= 0)
        {
            return;
        }

        var selected = AirtimeLabelLayout.SelectVisibleLabels(
            airtimeLabels
                .Select((label, index) => new AirtimeLabelLayoutCandidate(
                    index,
                    label.CenterSeconds,
                    label.WidthPixels,
                    label.DurationSeconds))
                .ToArray(),
            visibleMinimumSeconds,
            visibleMaximumSeconds,
            dataAreaWidthPixels);

        for (var i = 0; i < selected.Length; i++)
        {
            airtimeLabels[i].Text.IsVisible = selected[i];
        }
    }

    private static double EstimateLabelWidthPixels(string content) =>
        content.Length * AirtimeLabelFontSize * AirtimeLabelAverageCharacterWidthFactor;

    private sealed record AirtimeLabel(
        Text Text,
        double CenterSeconds,
        double DurationSeconds,
        double WidthPixels);
}

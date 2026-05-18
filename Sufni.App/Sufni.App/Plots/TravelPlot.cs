using System;
using System.Collections.Generic;
using ScottPlot;
using Sufni.App.Theming;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class TravelPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
{
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
                "0.#"));
        }

        if (telemetryData.Rear.Present)
        {
            series.Add(new RecordedTimeSeries(
                "Rear",
                "mm",
                RearColor,
                new SampledValues(telemetryData.Rear.Travel, telemetryData.Metadata.SampleRate),
                "0.#"));
        }

        LoadTimeSeries(new RecordedTimeSeriesData(
            "Travel (mm)",
            "No travel data",
            telemetryData.Metadata.Duration,
            series,
            new RecordedTimeSeriesValueRange(maxTravel, 0),
            telemetryData,
            ShowLegendWhenSingleSource: true));
    }

    protected override void AddTimeSeriesOverlays(RecordedTimeSeriesData data)
    {
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
            AddLabel($"{timeSpan:0.##}s air", airtime.Start + timeSpan / 2, maxTravel - 10,
                0, 0, Alignment.LowerCenter);
        }
    }
}

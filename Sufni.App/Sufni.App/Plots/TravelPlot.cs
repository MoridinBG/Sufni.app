using System;
using System.Collections.Generic;
using ScottPlot;
using Sufni.App.Models;
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

        var valueRange = new RecordedTimeSeriesValueRange(maxTravel, 0);
        LoadTimeSeries(new RecordedTimeSeriesData(
            "Travel (mm)",
            "No travel data",
            telemetryData.Metadata.Duration,
            series,
            valueRange,
            telemetryData,
            [
                RecordedTimeRangeOverlayFactory.CreateAirtimeRegistration(
                    telemetryData.Airtimes,
                    PlotTheme,
                    includeLabels: true,
                    labelY: GetAirtimeLabelY(valueRange)),
            ],
            ShowLegendWhenSingleSource: true,
            EnableInteractiveLegend: true,
            InteractiveLegendRowId: TelemetryGraphRowIds.Travel));
    }
}

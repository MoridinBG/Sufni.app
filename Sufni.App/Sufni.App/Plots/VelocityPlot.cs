using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using Sufni.App.Models;
using Sufni.App.Theming;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class VelocityPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
{
    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        var minimum = 0.0;
        var maximum = 0.0;
        var series = new List<RecordedTimeSeries>();

        if (telemetryData.Front.Present)
        {
            var fullVelocity = telemetryData.Front.Velocity.Select(v => v / 1000).ToArray();
            series.Add(new RecordedTimeSeries(
                "Front",
                "m/s",
                FrontColor,
                CreateSegmentAwareValues(telemetryData.Front, fullVelocity, telemetryData.Metadata.SampleRate),
                "0.###",
                SourceKey: TelemetrySourceKeys.Front));
            minimum = fullVelocity.Min();
            maximum = fullVelocity.Max();
        }

        if (telemetryData.Rear.Present)
        {
            var fullVelocity = telemetryData.Rear.Velocity.Select(v => v / 1000).ToArray();
            series.Add(new RecordedTimeSeries(
                "Rear",
                "m/s",
                RearColor,
                CreateSegmentAwareValues(telemetryData.Rear, fullVelocity, telemetryData.Metadata.SampleRate),
                "0.###",
                SourceKey: TelemetrySourceKeys.Rear));
            minimum = Math.Min(minimum, fullVelocity.Min());
            maximum = Math.Max(maximum, fullVelocity.Max());
        }

        LoadTimeSeries(new RecordedTimeSeriesData(
            "Velocity (m/s)",
            "No velocity data",
            telemetryData.Metadata.Duration,
            series,
            new RecordedTimeSeriesValueRange(minimum, maximum),
            telemetryData,
            [
                RecordedTimeRangeOverlayFactory.CreateAirtimeRegistration(telemetryData.Airtimes, PlotTheme),
            ],
            ShowLegendWhenSingleSource: true,
            EnableInteractiveLegend: true,
            InteractiveLegendRowId: TelemetryGraphRowIds.Velocity));
    }

    private static RecordedTimeSeriesValues CreateSegmentAwareValues(
        Suspension suspension,
        double[] denseVelocityMetersPerSecond,
        int sampleRate)
    {
        if (!suspension.HasGaps)
        {
            return new SampledValues(denseVelocityMetersPerSecond, sampleRate);
        }

        return new SegmentedValues(
            suspension.Segments
                .Select(segment => CreateExplicitSegment(
                    segment,
                    sampleRate,
                    segment.Velocity.Select(value => value / 1000).ToArray()))
                .Where(segment => segment.YValues.Length > 0)
                .ToArray());
    }

    private static ExplicitValues CreateExplicitSegment(
        ProcessedSuspensionSegment segment,
        int sampleRate,
        double[] values)
    {
        var xValues = new double[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            xValues[index] = segment.StartSeconds + index / (double)sampleRate;
        }

        return new ExplicitValues(xValues, values);
    }
}

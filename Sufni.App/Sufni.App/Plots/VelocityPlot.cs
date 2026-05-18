using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
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
                new SampledValues(fullVelocity, telemetryData.Metadata.SampleRate),
                "0.###"));
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
                new SampledValues(fullVelocity, telemetryData.Metadata.SampleRate),
                "0.###"));
            minimum = Math.Min(minimum, fullVelocity.Min());
            maximum = Math.Max(maximum, fullVelocity.Max());
        }

        LoadTimeSeries(new RecordedTimeSeriesData(
            "Velocity (m/s)",
            "No velocity data",
            telemetryData.Metadata.Duration,
            series,
            new RecordedTimeSeriesValueRange(minimum, maximum),
            telemetryData));
    }
}

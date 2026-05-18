using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using Sufni.App.Services.Imu;
using Sufni.App.Theming;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class ImuPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
{
    private const string Title = "Vibration RMS (g)";

    public static readonly Color FrameColor = SufniThemes.SignalSeries.ImuFrame.ToScottPlotColor();

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        if (telemetryData.ImuData == null || telemetryData.ImuData.Records.Count == 0 || telemetryData.ImuData.ActiveLocations.Count == 0)
        {
            ShowEmptyState(telemetryData.Metadata.Duration);
            return;
        }

        var displaySeries = ImuDisplaySignalProcessor.ProcessRecorded(telemetryData.ImuData);
        var maxVal = 0.0;
        var hasData = false;
        var series = new List<RecordedTimeSeries>();

        foreach (var vibrationSeries in displaySeries.VibrationSeries.OrderBy(item => item.LocationId))
        {
            if (vibrationSeries.RmsG.Length == 0)
            {
                continue;
            }

            maxVal = Math.Max(maxVal, vibrationSeries.RmsG.Max());
            hasData = true;

            // 0=Frame, 1=Fork (Front), 2=Shock (Rear)
            var (label, color) = vibrationSeries.LocationId switch
            {
                0 => ("Frame", FrameColor),
                1 => ("Fork", FrontColor),
                2 => ("Shock", RearColor),
                _ => ($"Location {vibrationSeries.LocationId}", Colors.Gray)
            };
            series.Add(new RecordedTimeSeries(
                label,
                "g",
                color,
                new SampledValues(vibrationSeries.RmsG, telemetryData.ImuData.SampleRate),
                "0.###"));
        }

        LoadTimeSeries(new RecordedTimeSeriesData(
            Title,
            "No IMU data",
            telemetryData.Metadata.Duration,
            series,
            hasData ? new RecordedTimeSeriesValueRange(0, maxVal) : null,
            telemetryData));
    }

    private void ShowEmptyState(double durationSeconds)
    {
        LoadTimeSeries(new RecordedTimeSeriesData(
            Title,
            "No IMU data",
            durationSeconds,
            []));
    }
}

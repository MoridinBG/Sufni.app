using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using Sufni.Telemetry;

using Sufni.App.LiveDaq.Services.Imu;
using Sufni.App.Sessions.Plots;
using Sufni.App.Theming;
using Sufni.App.Acquisition.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.LiveDaq.Plots;

public class ImuPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
{
    private const string Title = "Vibration RMS (g)";

    public static readonly Color FrameColor = SufniThemes.SignalSeries.ImuFrame.ToScottPlotColor();

    public void LoadProjection(TelemetryData telemetryData, RecordedImuDisplaySeries displaySeries)
    {
        if (telemetryData.ImuData == null || !telemetryData.ImuData.HasSamples || telemetryData.ImuData.ActiveLocations.Count == 0)
        {
            ShowEmptyState(telemetryData.Metadata.Duration);
            return;
        }

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
                CreateValues(telemetryData.ImuData, vibrationSeries),
                "0.###",
                SourceKey: TelemetrySourceKeys.ImuLocation(vibrationSeries.LocationId)));
        }

        LoadTimeSeries(new RecordedTimeSeriesData(
            Title,
            "No IMU data",
            telemetryData.Metadata.Duration,
            series,
            hasData ? new RecordedTimeSeriesValueRange(0, maxVal) : null,
            telemetryData,
            [
                RecordedTimeRangeOverlayFactory.CreateAirtimeRegistration(telemetryData.Airtimes, PlotTheme),
            ],
            EnableInteractiveLegend: true,
            InteractiveLegendRowId: SignalRowIds.Imu));
    }

    private void ShowEmptyState(double durationSeconds)
    {
        LoadTimeSeries(new RecordedTimeSeriesData(
            Title,
            "No IMU data",
            durationSeconds,
            []));
    }

    private static RecordedTimeSeriesValues CreateValues(RawImuData imuData, ImuVibrationSeries vibrationSeries)
    {
        if (!imuData.HasGaps)
        {
            return new SampledValues(vibrationSeries.RmsG, imuData.SampleRate);
        }

        if (vibrationSeries.Segments.Count == 0)
        {
            return new ExplicitValues(vibrationSeries.Times, vibrationSeries.RmsG);
        }

        return new SegmentedValues(
            vibrationSeries.Segments
                .Select(segment => new ExplicitValues(segment.Times, segment.Values))
                .ToArray());
    }
}

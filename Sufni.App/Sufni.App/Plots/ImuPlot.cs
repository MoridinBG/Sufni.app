using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class ImuPlot(Plot plot) : RecordedTimeSeriesPlot(plot)
{
    private const string Title = "IMU Acceleration (g)";

    public static readonly Color FrameColor = Color.FromHex("#fc8d59"); // Orange

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        if (telemetryData.ImuData == null || telemetryData.ImuData.Records.Count == 0 || telemetryData.ImuData.ActiveLocations.Count == 0)
        {
            ShowEmptyState(telemetryData.Metadata.Duration);
            return;
        }

        var imuData = telemetryData.ImuData;
        var activeLocations = imuData.ActiveLocations;
        var records = imuData.Records;
        var locationCount = activeLocations.Count;

        var signals = new Dictionary<byte, List<double>>();
        foreach (var loc in activeLocations)
        {
            signals[loc] = [];
        }

        // De-interleave records and convert to Gs using metadata
        for (int i = 0; i < records.Count; i++)
        {
            var locIndex = i % locationCount;
            var locId = activeLocations[locIndex];
            var record = records[i];

            var meta = imuData.Meta.FirstOrDefault(m => m.LocationId == locId);
            if (meta == null) continue;

            double accelScale = meta.AccelLsbPerG;
            if (accelScale == 0) accelScale = 1.0; // Fallback if meta is missing/zero

            double ax = (double)record.Ax / accelScale;
            double ay = (double)record.Ay / accelScale;
            double az = (double)record.Az / accelScale;

            // Remove gravity from Z axis
            az -= 1.0;

            // Calculate magnitude of the linear acceleration
            double mag = Math.Sqrt(ax * ax + ay * ay + az * az);
            signals[locId].Add(mag);
        }
        double minVal = 0.0;
        double maxVal = 0.0;
        bool hasData = false;
        var series = new List<RecordedTimeSeries>();

        foreach (var locId in signals.Keys.OrderBy(k => k))
        {
            var fullData = signals[locId].ToArray();
            if (fullData.Length == 0) continue;

            if (!hasData)
            {
                minVal = fullData.Min();
                maxVal = fullData.Max();
                hasData = true;
            }
            else
            {
                minVal = Math.Min(minVal, fullData.Min());
                maxVal = Math.Max(maxVal, fullData.Max());
            }

            // 0=Frame, 1=Fork (Front), 2=Shock (Rear)
            var (label, color) = locId switch
            {
                0 => ("Frame", FrameColor),
                1 => ("Fork", FrontColor),
                2 => ("Shock", RearColor),
                _ => ($"Location {locId}", Colors.Gray)
            };
            series.Add(new RecordedTimeSeries(
                label,
                "g",
                color,
                new SampledValues(fullData, imuData.SampleRate),
                "0.###"));
        }

        if (!hasData)
        {
            minVal = 0;
            maxVal = 1;
        }

        LoadTimeSeries(new RecordedTimeSeriesData(
            Title,
            "No IMU data",
            telemetryData.Metadata.Duration,
            series,
            new RecordedTimeSeriesValueRange(minVal, maxVal),
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

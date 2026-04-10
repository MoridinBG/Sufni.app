using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class ImuPlot(Plot plot) : TelemetryPlot(plot)
{
    public VerticalLine? CursorLine { get; set; }

    protected Color FrameColor = Color.FromHex("#fc8d59"); // Orange

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        base.LoadTelemetryData(telemetryData);

        Plot.Axes.Title.Label.Text = "IMU Acceleration (g)";
        Plot.Layout.Fixed(new PixelPadding(40, 40, 40, 40));
        Plot.Axes.Right.TickLabelStyle.ForeColor = Color.FromHex("#D0D0D0");
        Plot.Axes.Right.TickLabelStyle.Bold = false;
        Plot.Axes.Right.TickLabelStyle.FontSize = 12;
        Plot.Axes.Right.MajorTickStyle.Length = 0;
        Plot.Axes.Right.MinorTickStyle.Length = 0;
        Plot.Axes.Right.MajorTickStyle.Width = 0;
        Plot.Axes.Right.MinorTickStyle.Width = 0;

        CursorLine = Plot.Add.VerticalLine(double.NaN);
        CursorLine.LineWidth = 1;
        CursorLine.LineColor = Colors.LightGray;

        if (telemetryData.ImuData == null || telemetryData.ImuData.Records.Count == 0 || telemetryData.ImuData.ActiveLocations.Count == 0)
        {
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

        var step = 1.0 / imuData.SampleRate;

        double minVal = 0.0;
        double maxVal = 0.0;
        bool hasData = false;

        foreach (var locId in signals.Keys.OrderBy(k => k))
        {
            var data = signals[locId].ToArray();
            if (data.Length == 0) continue;

            if (!hasData)
            {
                minVal = data.Min();
                maxVal = data.Max();
                hasData = true;
            }
            else
            {
                minVal = Math.Min(minVal, data.Min());
                maxVal = Math.Max(maxVal, data.Max());
            }

            // 0=Frame, 1=Fork (Front), 2=Shock (Rear)
            Color color = locId switch
            {
                0 => FrameColor,
                1 => FrontColor,
                2 => RearColor,
                _ => Colors.Gray
            };

            var signal = Plot.Add.Signal(data, step, color);
            signal.Axes.XAxis = Plot.Axes.Bottom;
            signal.Axes.YAxis = Plot.Axes.Left;
            signal.LineWidth = 2.0f;
        }

        if (!hasData)
        {
            minVal = 0;
            maxVal = 1;
        }

        // Lock the vertical, and set limits on the horizontal axis
        var rule = new LockedVerticalSoftLockedHorizontalRule(Plot.Axes.Bottom, Plot.Axes.Left,
            0, telemetryData.Metadata.Duration, minVal, maxVal);
        Plot.Axes.Rules.Add(rule);

        var ruleRight = new LockedVerticalSoftLockedHorizontalRule(Plot.Axes.Bottom, Plot.Axes.Right,
            0, telemetryData.Metadata.Duration, minVal, maxVal);
        Plot.Axes.Rules.Add(ruleRight);

        // Maximize tick numbers
        ScottPlot.TickGenerators.NumericAutomatic tickGenTime = new()
        {
            TargetTickCount = 20,
            LabelFormatter = d => $"{d:0.###}"
        };
        Plot.Axes.Bottom.TickGenerator = tickGenTime;

        ScottPlot.TickGenerators.NumericAutomatic tickGenValue = new()
        {
            MinimumTickSpacing = 0.1f
        };
        Plot.Axes.Left.TickGenerator = tickGenValue;
        Plot.Axes.Right.TickGenerator = tickGenValue;
    }
}

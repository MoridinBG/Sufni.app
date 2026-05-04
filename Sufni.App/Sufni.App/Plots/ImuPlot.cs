using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public class ImuPlot(Plot plot) : TelemetryPlot(plot)
{
    private readonly List<CursorReadoutSeries> cursorSeries = [];
    private double cursorDurationSeconds;

    public VerticalLine? CursorLine { get; set; }

    public static readonly Color FrameColor = Color.FromHex("#fc8d59"); // Orange

    protected override void SetCursorLinePosition(double position)
    {
        if (CursorLine is not null)
        {
            CursorLine.Position = position;
        }
    }

    protected override CursorReadout? GetCursorReadout(double position)
    {
        return CreateCursorReadout(position, cursorDurationSeconds, cursorSeries);
    }

    public override void LoadTelemetryData(TelemetryData telemetryData)
    {
        base.LoadTelemetryData(telemetryData);
        CursorLine = null;
        cursorSeries.Clear();
        cursorDurationSeconds = telemetryData.Metadata.Duration;

        if (telemetryData.ImuData == null || telemetryData.ImuData.Records.Count == 0 || telemetryData.ImuData.ActiveLocations.Count == 0)
        {
            ShowEmptyState();
            return;
        }

        Plot.Axes.Title.Label.Text = "IMU Acceleration (g)";
        Plot.Layout.Fixed(new PixelPadding(40, 40, 40, 40));
        ConfigureRightAxisStyle();

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

            var (data, step) = PrepareDisplaySignal(fullData, imuData.SampleRate);

            // 0=Frame, 1=Fork (Front), 2=Shock (Rear)
            var (label, color) = locId switch
            {
                0 => ("Frame", FrameColor),
                1 => ("Fork", FrontColor),
                2 => ("Shock", RearColor),
                _ => ($"Location {locId}", Colors.Gray)
            };
            cursorSeries.Add(CursorReadoutSeries.FromRegularSamples(
                label,
                "g",
                color,
                data,
                step,
                telemetryData.Metadata.Duration,
                "0.###"));

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

        ConfigureTimeTicks();
        ConfigureSymmetricValueTicks(0.1f);

        AddMarkerLines(telemetryData);

        CursorLine = Plot.Add.VerticalLine(double.NaN);
        CursorLine.LineWidth = 1;
        CursorLine.LineColor = Colors.LightGray;
    }

    private void ShowEmptyState()
    {
        Plot.Axes.Title.Label.Text = "IMU Acceleration (g)";
        Plot.Layout.Fixed(new PixelPadding(40, 40, 40, 40));
        Plot.Axes.SetLimits(0, 1, 0, 1);

        var text = Plot.Add.Text("No IMU data", 0.5, 0.5);
        text.LabelFontColor = Color.FromHex("#fefefe");
        text.LabelFontSize = 13;
        text.LabelAlignment = Alignment.MiddleCenter;
    }
}

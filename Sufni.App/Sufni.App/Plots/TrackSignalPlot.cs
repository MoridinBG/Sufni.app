using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public enum TrackSignalKind
{
    Speed,
    Elevation
}

public class TrackSignalPlot(Plot plot) : TelemetryPlot(plot)
{
    public VerticalLine? CursorLine { get; set; }

    public override void SetCursorPosition(double position)
    {
        if (CursorLine is not null)
        {
            CursorLine.Position = position;
        }
    }

    public void LoadTrackData(
        IReadOnlyList<TrackPoint> points,
        TrackTimelineContext context,
        TelemetryData? telemetryData,
        TrackSignalKind kind)
    {
        CursorLine = null;

        var samples = points
            .Select(point => (X: point.Time - context.OriginSeconds, Y: GetSignalValue(point, kind)))
            .Where(sample => double.IsFinite(sample.X)
                             && sample.Y is { } y
                             && double.IsFinite(y))
            .Select(sample => (sample.X, Y: sample.Y!.Value))
            .ToArray();

        ConfigureAxes(kind);

        if (samples.Length < 2)
        {
            ShowEmptyState(kind);
            return;
        }

        var xValues = samples.Select(sample => sample.X).ToArray();
        var yValues = TelemetryDisplaySmoothing.Apply(
            samples.Select(sample => sample.Y).ToArray(),
            SmoothingLevel);

        var signal = Plot.Add.Scatter(xValues, yValues);
        signal.Color = Color.FromHex("#ffffbf");
        signal.LineWidth = 2.0f;
        signal.MarkerStyle.IsVisible = false;

        var (minimum, maximum) = GetVerticalRange(yValues);
        Plot.Axes.SetLimits(0, context.DurationSeconds, minimum, maximum);
        Plot.Axes.Rules.Add(new LockedVerticalSoftLockedHorizontalRule(
            Plot.Axes.Bottom,
            Plot.Axes.Left,
            0,
            context.DurationSeconds,
            minimum,
            maximum));

        ConfigureTimeTicks();

        if (telemetryData is not null)
        {
            AddMarkerLines(telemetryData);
        }

        CursorLine = Plot.Add.VerticalLine(double.NaN);
        CursorLine.LineWidth = 1;
        CursorLine.LineColor = Colors.LightGray;
    }

    private static double? GetSignalValue(TrackPoint point, TrackSignalKind kind)
    {
        return kind switch
        {
            TrackSignalKind.Speed => point.Speed * 3.6,
            TrackSignalKind.Elevation => point.Elevation,
            _ => null
        };
    }

    private void ConfigureAxes(TrackSignalKind kind)
    {
        var (title, yAxisLabel) = kind switch
        {
            TrackSignalKind.Speed => ("Speed", "Speed (km/h)"),
            TrackSignalKind.Elevation => ("Elevation", "Elevation (m)"),
            _ => ("Track", "Value")
        };

        Plot.Axes.Title.Label.Text = title;
        Plot.Layout.Fixed(new PixelPadding(40, 40, 40, 40));
        SetAxisLabels("Time (s)", yAxisLabel);
    }

    private void ShowEmptyState(TrackSignalKind kind)
    {
        Plot.Axes.SetLimits(0, 1, 0, 1);

        var message = kind switch
        {
            TrackSignalKind.Speed => "No speed data",
            TrackSignalKind.Elevation => "No elevation data",
            _ => "No track data"
        };

        var text = Plot.Add.Text(message, 0.5, 0.5);
        text.LabelFontColor = Color.FromHex("#fefefe");
        text.LabelFontSize = 13;
        text.LabelAlignment = Alignment.MiddleCenter;
    }

    private static (double Minimum, double Maximum) GetVerticalRange(IReadOnlyList<double> values)
    {
        var minimum = values.Min();
        var maximum = values.Max();
        var span = maximum - minimum;
        var padding = Math.Max(Math.Abs(span) * 0.05, 1.0);

        return (minimum - padding, maximum + padding);
    }
}

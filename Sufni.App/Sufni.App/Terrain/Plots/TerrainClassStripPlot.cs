using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using ScottPlot;
using ScottPlot.Plottables;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Sessions.Plots;
using Sufni.App.Shared.Plots;
using Sufni.App.Terrain.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Plots;

/// <summary>Two labelled lanes of class runs on the recorded timeline: grade above, roughness below.</summary>
public sealed class TerrainClassStripPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
{
    private const double LaneGap = 0.04;
    private const float RunNameFontSize = 11;
    private readonly List<RunName> runNames = [];
    private TerrainPresentation? terrain;

    private sealed record RunName(Text Text, double StartSeconds, double EndSeconds, double Lane);

    public void Load(TerrainPresentation presentation, TelemetryData telemetry)
    {
        terrain = presentation;
        runNames.Clear();
        ConfigureTimeSeriesFrame("Terrain");
        Plot.Axes.Left.IsVisible = false;
        Plot.Axes.Right.IsVisible = false;
        var duration = telemetry.Metadata.Duration;
        Plot.Axes.SetLimits(0, duration, 0, 2);
        AddMirroredTimeSeriesAxisRules(0, duration, 0, 2);
        var scales = PlotTheme.Series.Terrain;
        DrawLane(presentation.GradeRuns, 1, band => (scales.ColorFor(band), TerrainClassText.Name(band)));
        DrawLane(presentation.RoughnessRuns, 0, level => (scales.ColorFor(level), TerrainClassText.Name(level)));
        AddLaneLabel("Grade", Alignment.UpperLeft);
        AddLaneLabel("Roughness", Alignment.LowerLeft);
        var airtime = RecordedTimeRangeOverlayFactory.CreateAirtimeRegistration(telemetry.Airtimes, PlotTheme);
        SetRangeOverlaySet(airtime.Id, airtime.Set);
        SetRangeOverlayVisibility(airtime.Id, airtime.IsVisible);
        AddMarkerLines(telemetry);
        CursorLine = AddTimeSeriesCursorLine();
    }

    /// <summary>Shows each run name centred on the visible part of its run, where the name fits.</summary>
    public void LayoutRunNames(double visibleMinimumSeconds, double visibleMaximumSeconds, double dataAreaWidthPixels)
    {
        var pixelsPerSecond = dataAreaWidthPixels / Math.Max(visibleMaximumSeconds - visibleMinimumSeconds, double.Epsilon);
        foreach (var name in runNames)
        {
            var start = Math.Max(name.StartSeconds, visibleMinimumSeconds);
            var end = Math.Min(name.EndSeconds, visibleMaximumSeconds);
            // Rendered width is approximated, so a name needs some margin inside its run.
            var needed = name.Text.LabelText.Length * RunNameFontSize * 0.6 + 8;
            name.Text.IsVisible = (end - start) * pixelsPerSecond >= needed;
            name.Text.Location = new((start + end) / 2, name.Lane + 0.5);
        }
    }

    protected override CursorReadout? GetCursorReadout(double position)
    {
        if (terrain is null || !double.IsFinite(position)) return null;
        var scales = PlotTheme.Series.Terrain;
        var lines = new List<CursorReadoutLine>();
        if (RunAt(terrain.GradeRuns, position) is { } band)
        {
            var degrees = ValueAt(terrain.Grade, position);
            var text = degrees is { } value ? $"{TerrainClassText.Name(band)} · {value.ToString("0.#", CultureInfo.InvariantCulture)}°" : TerrainClassText.Name(band);
            lines.Add(new("Grade", degrees ?? 0, "°", scales.ColorFor(band).ToScottPlotColor(), DisplayValue: text));
        }
        if (RunAt(terrain.RoughnessRuns, position) is { } level)
            lines.Add(new("Roughness", 0, string.Empty, scales.ColorFor(level).ToScottPlotColor(),
                DisplayValue: TerrainClassText.Name(level)));
        if (ValueAt(terrain.Frame, position) is { } frame)
            lines.Add(new("Frame", frame, "g", PlotTheme.Series.ImuFrame.ToScottPlotColor()));
        if (ValueAt(terrain.Lowers, position) is { } lowers)
            lines.Add(new("Lowers", lowers, "g", PlotTheme.Series.ImuFork.ToScottPlotColor()));
        return lines.Count == 0 ? null : new CursorReadout(position, position, 1, lines);
    }

    private void DrawLane<TClass>(ImmutableArray<TerrainRun<TClass>> runs, double lane,
        Func<TClass, (Avalonia.Media.Color Colour, string Name)> style) where TClass : struct, Enum
    {
        foreach (var run in runs)
        {
            var (colour, name) = style(run.Class);
            var rectangle = Plot.Add.Rectangle(run.StartSeconds, run.EndSeconds, lane + LaneGap, lane + 1 - LaneGap);
            rectangle.FillColor = colour.ToScottPlotColor();
            rectangle.LineWidth = 0;
            var text = Plot.Add.Text(name, (run.StartSeconds + run.EndSeconds) / 2, lane + 0.5);
            text.LabelFontSize = RunNameFontSize;
            text.LabelAlignment = Alignment.MiddleCenter;
            text.LabelFontColor = IsLight(colour) ? Colors.Black : Colors.White;
            text.IsVisible = false;
            runNames.Add(new(text, run.StartSeconds, run.EndSeconds, lane));
        }
    }

    private void AddLaneLabel(string label, Alignment alignment)
    {
        var annotation = Plot.Add.Annotation(label, alignment);
        annotation.LabelFontSize = 12;
        annotation.LabelFontColor = PlotTheme.InPlotLabelText.ToScottPlotColor();
        annotation.LabelBackgroundColor = PlotTheme.Legend.Background.ToScottPlotColor().WithAlpha(0.85);
        annotation.LabelBorderColor = Colors.Transparent;
        annotation.LabelShadowColor = Colors.Transparent;
        annotation.OffsetX = 2;
        annotation.OffsetY = 2;
    }

    private static bool IsLight(Avalonia.Media.Color colour) =>
        0.2126 * colour.R + 0.7152 * colour.G + 0.0722 * colour.B > 150;

    private static TClass? RunAt<TClass>(ImmutableArray<TerrainRun<TClass>> runs, double seconds) where TClass : struct, Enum
    {
        int lo = 0, hi = runs.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (runs[mid].EndSeconds <= seconds) lo = mid + 1;
            else hi = mid;
        }
        return lo < runs.Length && runs[lo].StartSeconds <= seconds ? runs[lo].Class : null;
    }

    // Sections hold window midpoints, so the nearest midpoint inside a section stands for the window under the cursor.
    private static double? ValueAt(ImmutableArray<TerrainSignalSection> sections, double seconds)
    {
        foreach (var section in sections)
        {
            if (section.Seconds.IsEmpty || seconds < section.Seconds[0] || seconds > section.Seconds[^1]) continue;
            var index = section.Seconds.BinarySearch(seconds);
            if (index < 0)
            {
                index = ~index;
                if (index > 0 && seconds - section.Seconds[index - 1] < section.Seconds[index] - seconds) index--;
            }
            return section.Values[index];
        }
        return null;
    }
}

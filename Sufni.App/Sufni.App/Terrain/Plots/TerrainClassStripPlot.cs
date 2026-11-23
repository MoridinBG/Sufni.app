using System;
using System.Collections.Immutable;
using Avalonia.Media;
using ScottPlot;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Sessions.Plots;
using Sufni.App.Shared.Plots;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Plots;

internal static class TerrainColors
{
    public static Avalonia.Media.Color ColorFor(this SufniTerrainRamp ramp, TerrainLevel level) => level switch
    {
        TerrainLevel.Low => ramp.Low, TerrainLevel.Middle => ramp.Middle, TerrainLevel.High => ramp.High,
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };
}

public sealed class TerrainClassStripPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
{
    public void Load(TerrainPresentation terrain, TelemetryData telemetry)
    {
        ConfigureTimeSeriesFrame("Terrain");
        Plot.Axes.Left.IsVisible = false;
        Plot.Axes.Right.IsVisible = false;
        var duration = telemetry.Metadata.Duration;
        Plot.Axes.SetLimits(0, duration, 0, 2);
        AddMirroredTimeSeriesAxisRules(0, duration, 0, 2);
        DrawBand(terrain.RoughnessRuns, 1);
        DrawBand(terrain.SteepnessRuns, 0);
        var airtime = RecordedTimeRangeOverlayFactory.CreateAirtimeRegistration(telemetry.Airtimes, PlotTheme);
        SetRangeOverlaySet(airtime.Id, airtime.Set);
        SetRangeOverlayVisibility(airtime.Id, airtime.IsVisible);
        AddMarkerLines(telemetry);
        CursorLine = AddTimeSeriesCursorLine();
    }

    private void DrawBand(ImmutableArray<TerrainClassRun> runs, int bottom)
    {
        foreach (var run in runs)
        {
            var rectangle = Plot.Add.Rectangle(run.StartSeconds, run.EndSeconds, bottom, bottom + 1);
            rectangle.FillColor = PlotTheme.Series.Terrain.ColorFor(run.Level).ToScottPlotColor();
            rectangle.LineWidth = 0;
        }
    }
}

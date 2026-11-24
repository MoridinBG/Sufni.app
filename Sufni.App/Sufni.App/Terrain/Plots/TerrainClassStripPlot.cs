using System.Collections.Immutable;
using Avalonia.Media;
using ScottPlot;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Sessions.Plots;
using Sufni.App.Shared.Plots;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Plots;

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
        var scales = PlotTheme.Series.Terrain;
        DrawBand(terrain.RoughnessRuns.Colour(scales.ColorFor), 1);
        DrawBand(terrain.GradeRuns.Colour(scales.ColorFor), 0);
        var airtime = RecordedTimeRangeOverlayFactory.CreateAirtimeRegistration(telemetry.Airtimes, PlotTheme);
        SetRangeOverlaySet(airtime.Id, airtime.Set);
        SetRangeOverlayVisibility(airtime.Id, airtime.IsVisible);
        AddMarkerLines(telemetry);
        CursorLine = AddTimeSeriesCursorLine();
    }

    private void DrawBand(ImmutableArray<TerrainColourRun> runs, int bottom)
    {
        foreach (var run in runs)
        {
            var rectangle = Plot.Add.Rectangle(run.StartSeconds, run.EndSeconds, bottom, bottom + 1);
            rectangle.FillColor = run.Colour.ToScottPlotColor();
            rectangle.LineWidth = 0;
        }
    }
}

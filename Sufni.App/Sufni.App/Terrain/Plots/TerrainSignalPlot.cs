using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ScottPlot;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Sessions.Plots;
using Sufni.App.Shared.Plots;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Plots;

public enum TerrainSignalKind { Roughness, Grade }

public sealed class TerrainSignalPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
{
    public void Load(TerrainPresentation terrain, TelemetryData telemetry, TerrainSignalKind kind)
    {
        var series = new List<RecordedTimeSeries>();
        if (kind == TerrainSignalKind.Roughness)
        {
            Add(terrain.Frame, "Frame", "g", PlotTheme.Series.ImuFrame);
            Add(terrain.Lowers, "Lowers", "g", PlotTheme.Series.ImuFork);
        }
        else Add(terrain.Grade, "Grade", "°", PlotTheme.Series.GpsElevation);
        LoadTimeSeries(new(kind.ToString(), "No measured windows.", telemetry.Metadata.Duration, series, MarkerSource: telemetry,
            InitialRangeOverlays: [RecordedTimeRangeOverlayFactory.CreateAirtimeRegistration(telemetry.Airtimes, PlotTheme)]));

        void Add(ImmutableArray<TerrainSignalSection> sections, string label, string unit, Avalonia.Media.Color color)
        {
            if (sections.IsEmpty) return;
            var segments = sections.Select(s => new ExplicitValues(s.Seconds.ToArray(), s.Values.ToArray())).ToArray();
            series.Add(new(label, unit, color.ToScottPlotColor(), new SegmentedValues(segments)));
        }
    }
}

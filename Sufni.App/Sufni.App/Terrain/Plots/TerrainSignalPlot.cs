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

public enum TerrainSignalKind { Roughness, Grade, PedalBob }

/// <summary>
/// A measured terrain curve over light shading in its lane colours, with guide lines at the class boundaries.
/// </summary>
public sealed class TerrainSignalPlot(Plot plot, SufniTheme? theme = null) : RecordedTimeSeriesPlot(plot, theme)
{
    private const double ShadingAlpha = 0.18;
    private const double GuideAlpha = 0.6;

    public void Load(TerrainPresentation terrain, TelemetryData telemetry, TerrainSignalKind kind)
    {
        var series = new List<RecordedTimeSeries>();
        var scales = PlotTheme.Series.Terrain;
        if (kind == TerrainSignalKind.Roughness)
        {
            Add(terrain.Frame, "Frame", "g", PlotTheme.Series.ImuFrame);
            Add(terrain.Fork, "Fork", "g", PlotTheme.Series.ImuFork);
            if (series.Count > 0)
            {
                Shade(terrain.RoughnessRuns.Colour(scales.ColorFor));
                if (!terrain.Frame.IsEmpty) AddGuides(terrain.FrameEdges, PlotTheme.Series.ImuFrame.ToScottPlotColor());
                if (!terrain.Fork.IsEmpty) AddGuides(terrain.ForkEdges, PlotTheme.Series.ImuFork.ToScottPlotColor());
            }
        }
        else if (kind == TerrainSignalKind.PedalBob)
        {
            Add(terrain.PedalBob, "Rear bob", "mm", PlotTheme.Series.SuspensionRear);
        }
        else
        {
            Add(terrain.Grade, "Grade", "°", PlotTheme.Series.GpsElevation);
            if (series.Count > 0)
            {
                Shade(terrain.GradeRuns.Colour(scales.ColorFor));
                AddGuides(TerrainProfileCalculator.GradeBandEdges, PlotTheme.ReferenceLine.ToScottPlotColor());
                AddGuides([0], PlotTheme.InPlotLabelText.ToScottPlotColor(), LinePattern.Solid);
            }
        }
        var empty = kind == TerrainSignalKind.PedalBob ? "No pedalling detected." : "No measured windows.";
        LoadTimeSeries(new(kind.ToString(), empty, telemetry.Metadata.Duration, series, MarkerSource: telemetry,
            InitialRangeOverlays: [RecordedTimeRangeOverlayFactory.CreateAirtimeRegistration(telemetry.Airtimes, PlotTheme)]));
        if (kind == TerrainSignalKind.Grade && series.Count > 0)
        {
            AddDirectionLabel("▲ descent", Alignment.UpperLeft);
            AddDirectionLabel("▼ climb", Alignment.LowerLeft);
        }

        void Add(ImmutableArray<TerrainSignalSection> sections, string label, string unit, Avalonia.Media.Color color)
        {
            if (sections.IsEmpty) return;
            var segments = sections.Select(s => new ExplicitValues(s.Seconds.ToArray(), s.Values.ToArray())).ToArray();
            series.Add(new(label, unit, color.ToScottPlotColor(), new SegmentedValues(segments)));
        }
    }

    private void Shade(ImmutableArray<TerrainColourRun> runs)
    {
        foreach (var run in runs)
        {
            var span = Plot.Add.HorizontalSpan(run.StartSeconds, run.EndSeconds);
            span.FillStyle.Color = run.Colour.ToScottPlotColor().WithAlpha(ShadingAlpha);
            span.LineStyle.Width = 0;
            span.IsDraggable = false;
        }
    }

    private void AddGuides(IEnumerable<double> values, Color color, LinePattern? pattern = null)
    {
        foreach (var value in values)
        {
            Plot.Add.Plottable(new FixedHorizontalLine
            {
                Y = value,
                LineWidth = 1,
                LineColor = color.WithAlpha(GuideAlpha),
                LinePattern = pattern ?? LinePattern.DenselyDashed
            });
        }
    }

    private void AddDirectionLabel(string text, Alignment alignment)
    {
        var annotation = Plot.Add.Annotation(text, alignment);
        annotation.LabelFontSize = 11;
        annotation.LabelFontColor = PlotTheme.InPlotLabelText.ToScottPlotColor();
        annotation.LabelBackgroundColor = Colors.Transparent;
        annotation.LabelBorderColor = Colors.Transparent;
        annotation.LabelShadowColor = Colors.Transparent;
    }
}

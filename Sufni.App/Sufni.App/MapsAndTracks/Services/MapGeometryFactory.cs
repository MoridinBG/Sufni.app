using System.Collections.Immutable;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Terrain.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;

namespace Sufni.App.MapsAndTracks.Services;

/// <summary>Prepares renderable geometry off-thread; the requesting view owns the result.</summary>
public interface IMapGeometryFactory
{
    Task<MapGeometrySnapshot> BuildAsync(MapGeometryInput input, MapGeometrySnapshot? previous, CancellationToken cancellationToken);
}

public sealed record MapGeometryInput(
    IReadOnlyList<TrackPoint> FullTrack,
    IReadOnlyList<TrackPoint> SessionTrack,
    MatchedTrackOverlay? MatchedTrack,
    ImmutableArray<TerrainColourRun> TerrainRuns = default, double TerrainOriginSeconds = 0);

public sealed record MapGeometrySnapshot(
    MapGeometryInput Input,
    RenderedTrackGeometry FullTrack,
    RenderedTrackGeometry SessionTrack,
    IReadOnlyList<IFeature> MatchedFeatures);

public sealed record RenderedTrackGeometry(
    IReadOnlyList<TrackPoint> Points,
    IReadOnlyList<IFeature> LineFeatures,
    IReadOnlyList<IFeature> MarkerFeatures,
    TrackPointTimeIndex? TimeIndex);

internal sealed class MapGeometryFactory(IBackgroundTaskRunner background) : IMapGeometryFactory
{
    public Task<MapGeometrySnapshot> BuildAsync(MapGeometryInput input, MapGeometrySnapshot? previous, CancellationToken cancellationToken) =>
        background.RunAsync(() => new MapGeometrySnapshot(input,
            ReferenceEquals(input.FullTrack, previous?.Input.FullTrack) ? previous.FullTrack : BuildTrack(input.FullTrack, false),
            previous is not null && ReferenceEquals(input.SessionTrack, previous.Input.SessionTrack) &&
                input.TerrainRuns == previous.Input.TerrainRuns && input.TerrainOriginSeconds == previous.Input.TerrainOriginSeconds
                ? previous.SessionTrack : BuildTrack(input.SessionTrack, true, input.TerrainRuns, input.TerrainOriginSeconds),
            ReferenceEquals(input.MatchedTrack, previous?.Input.MatchedTrack) && previous is not null
                ? previous.MatchedFeatures : BuildMatched(input.MatchedTrack)), cancellationToken);

    private static RenderedTrackGeometry BuildTrack(IReadOnlyList<TrackPoint> points, bool buildTimeIndex,
        ImmutableArray<TerrainColourRun> runs = default, double origin = 0)
    {
        IReadOnlyList<IFeature> lines = points.Count < 2 ? [] : buildTimeIndex
            ? SessionLines(points, runs, origin)
            : [new GeometryFeature { Geometry = new LineString(points.Select(p => new Coordinate(p.X, p.Y)).ToArray()) }];
        IFeature[] markers = points.Count == 0 ? [] : [CreateMarker(points[0], "#229954"), CreateMarker(points[^1], "#E74C3C")];
        return new(points, lines, markers, buildTimeIndex ? new TrackPointTimeIndex(points) : null);
    }

    private static IReadOnlyList<IFeature> SessionLines(IReadOnlyList<TrackPoint> points,
        ImmutableArray<TerrainColourRun> runs, double origin)
    {
        if (runs.IsDefaultOrEmpty)
            return [PlainLine(points.Select(p => new Coordinate(p.X, p.Y)).ToArray())];
        var features = new List<IFeature>();
        var line = new List<Coordinate> { new(points[0].X, points[0].Y) };
        using var changes = Boundaries().GetEnumerator();
        var hasChange = changes.MoveNext();
        Avalonia.Media.Color? colour = null;
        for (var i = 1; i < points.Count; i++)
        {
            var a = points[i - 1];
            var b = points[i];
            while (hasChange && changes.Current.Time <= b.Time)
            {
                var (time, next) = changes.Current;
                if (time > a.Time)
                {
                    var t = (time - a.Time) / (b.Time - a.Time);
                    var border = new Coordinate(a.X + t * (b.X - a.X), a.Y + t * (b.Y - a.Y));
                    line.Add(border);
                    FinishLine();
                    line.Add(border);
                }
                colour = next;
                hasChange = changes.MoveNext();
            }
            if (line[^1].X != b.X || line[^1].Y != b.Y) line.Add(new(b.X, b.Y));
        }
        FinishLine();
        return features;

        IEnumerable<(double Time, Avalonia.Media.Color? Colour)> Boundaries()
        {
            for (var i = 0; !runs.IsDefault && i < runs.Length; i++)
            {
                var run = runs[i];
                yield return (origin + run.StartSeconds, run.Colour);
                if (i + 1 == runs.Length || runs[i + 1].StartSeconds != run.EndSeconds)
                    yield return (origin + run.EndSeconds, null);
            }
        }

        void FinishLine()
        {
            if (line.Count >= 2) features.Add(colour is { } c ? TerrainLine(line.ToArray(), c) : UnclassifiedLine(line.ToArray()));
            line.Clear();
        }
    }

    private static GeometryFeature PlainLine(Coordinate[] coordinates) =>
        Line(coordinates, new Pen(Color.FromString("#9e0142"), 5));

    private static GeometryFeature TerrainLine(Coordinate[] coordinates, Avalonia.Media.Color colour) =>
        Line(coordinates, new Pen(new Color(colour.R, colour.G, colour.B, colour.A), 5));

    // Grey alone would read as Flat or a climb level, so ground without a class is also thinner and dashed.
    private static GeometryFeature UnclassifiedLine(Coordinate[] coordinates)
    {
        var colour = SufniThemes.SignalSeries.Terrain.Unclassified;
        return Line(coordinates, new Pen(new Color(colour.R, colour.G, colour.B, colour.A), 3) { PenStyle = PenStyle.Dash });
    }

    private static GeometryFeature Line(Coordinate[] coordinates, Pen pen)
    {
        var feature = new GeometryFeature { Geometry = new LineString(coordinates) };
        feature.Styles.Add(new VectorStyle { Line = pen });
        return feature;
    }

    private static IReadOnlyList<IFeature> BuildMatched(MatchedTrackOverlay? matched)
    {
        var features = new List<IFeature>();
        if (matched is not null)
        {
            AddMatched(features, matched.SessionTrack, 3, 0.25);
            AddMatched(features, matched.SegmentTrack, 4, 0.7);
        }
        return features;
    }

    private static void AddMatched(List<IFeature> features, IReadOnlyList<TrackPoint> points, double width, double opacity)
    {
        var coordinates = points.Where(point => double.IsFinite(point.X) && double.IsFinite(point.Y))
            .Select(point => new Coordinate(point.X, point.Y)).ToArray();
        if (coordinates.Length < 2) return;
        var feature = new GeometryFeature { Geometry = new LineString(coordinates) };
        feature.Styles.Add(new VectorStyle { Line = new Pen(new Color(115, 199, 240, (int)Math.Round(255 * opacity)), width) });
        features.Add(feature);
    }

    private static PointFeature CreateMarker(TrackPoint point, string fillColor)
    {
        var feature = new PointFeature(point.X, point.Y);
        feature.Styles.Add(new SymbolStyle
        {
            SymbolType = SymbolType.Ellipse,
            Line = new Pen(Color.Black),
            Fill = new Brush(Color.FromString(fillColor)),
            SymbolScale = 0.5,
        });
        return feature;
    }
}

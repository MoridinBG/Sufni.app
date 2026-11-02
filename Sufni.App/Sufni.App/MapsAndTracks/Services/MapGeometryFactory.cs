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
    MatchedTrackOverlay? MatchedTrack);

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
            ReferenceEquals(input.SessionTrack, previous?.Input.SessionTrack) ? previous.SessionTrack : BuildTrack(input.SessionTrack, true),
            ReferenceEquals(input.MatchedTrack, previous?.Input.MatchedTrack) && previous is not null
                ? previous.MatchedFeatures : BuildMatched(input.MatchedTrack)), cancellationToken);

    private static RenderedTrackGeometry BuildTrack(IReadOnlyList<TrackPoint> points, bool buildTimeIndex)
    {
        var coordinates = points.Select(point => new Coordinate(point.X, point.Y)).ToArray();
        IFeature[] lines = coordinates.Length < 2 ? [] : [new GeometryFeature { Geometry = new LineString(coordinates) }];
        IFeature[] markers = points.Count == 0 ? [] : [CreateMarker(points[0], "#229954"), CreateMarker(points[^1], "#E74C3C")];
        return new(points, lines, markers, buildTimeIndex ? new TrackPointTimeIndex(points) : null);
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

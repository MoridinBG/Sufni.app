using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Mapsui.Projections;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Models;

namespace Sufni.App.MapsAndTracks.Services;

public interface IGpxTrackReader
{
    Task<GpxTrackReadResult> ReadAsync(PickedFile file, CancellationToken cancellationToken = default);
}

public abstract record GpxTrackReadResult
{
    private GpxTrackReadResult() { }
    public sealed record Read(IReadOnlyList<TrackPoint> Points) : GpxTrackReadResult;
    public sealed record Failed(GpxImportFailureReason Reason, string? Details = null) : GpxTrackReadResult;
}

/// <summary>Translates a picked GPX recording into timestamped projected track points.</summary>
internal sealed class GpxTrackReader : IGpxTrackReader
{
    private static readonly ILogger logger = Log.ForContext<GpxTrackReader>();

    public async Task<GpxTrackReadResult> ReadAsync(PickedFile file, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            var gpx = await reader.ReadToEndAsync(cancellationToken);
            var points = ParseGpx(gpx, out var coordinates);
            if (points.Count == 0)
            {
                logger.Warning("GPX file {FileName} has no valid track points", file.Name);
                return new GpxTrackReadResult.Failed(GpxImportFailureReason.InvalidFile);
            }
            TrackPointSeries.CalculateSpeed(points, coordinates);
            return new GpxTrackReadResult.Read(points.AsReadOnly());
        }
        catch (XmlException exception)
        {
            logger.Warning(exception, "Invalid GPX file {FileName}", file.Name);
            return new GpxTrackReadResult.Failed(GpxImportFailureReason.InvalidFile, exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Error(exception, "GPX file could not be read: {FileName}", file.Name);
            return new GpxTrackReadResult.Failed(GpxImportFailureReason.Import, exception.Message);
        }
    }

    private static List<TrackPoint> ParseGpx(string gpxData, out List<TrackPointGeoCoordinate> coordinates)
    {
        var points = new List<TrackPoint>();
        coordinates = [];

        XNamespace ns = "http://www.topografix.com/GPX/1/1";
        var xdoc = XDocument.Parse(gpxData);
        var trkpts = xdoc.Descendants(ns + "trkpt");

        foreach (var pt in trkpts)
        {
            var timeString = pt.Element(ns + "time")?.Value;
            if (string.IsNullOrEmpty(timeString) ||
                !DateTime.TryParse(timeString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var time))
            {
                continue;
            }

            if (!double.TryParse(pt.Attribute("lat")?.Value, CultureInfo.InvariantCulture, out var lat) ||
                !double.TryParse(pt.Attribute("lon")?.Value, CultureInfo.InvariantCulture, out var lon))
            {
                continue;
            }

            double? elevation = null;
            var elevationString = pt.Element(ns + "ele")?.Value;
            if (!string.IsNullOrEmpty(elevationString) &&
                double.TryParse(elevationString, CultureInfo.InvariantCulture, out var ele))
            {
                elevation = ele;
            }

            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            points.Add(new TrackPoint(new DateTimeOffset(time).ToUnixTimeMilliseconds() / 1000.0, mx, my, elevation));
            coordinates.Add(new TrackPointGeoCoordinate(lat, lon));
        }

        return points;
    }

}

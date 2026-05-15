using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

public enum TrackSignalKind
{
    Speed,
    Elevation
}

public class TrackSignalPlot(Plot plot) : RecordedTimeSeriesPlot(plot)
{
    private static readonly Color gpsQualityReadoutColor = Color.FromHex("#d0d6da");
    private readonly List<TrackSignalSample> cursorSamples = [];
    private TrackSignalKind loadedKind;

    public void LoadTrackData(
        IReadOnlyList<TrackPoint> points,
        TrackTimeRange context,
        TelemetryData? telemetryData,
        TrackSignalKind kind)
    {
        cursorSamples.Clear();
        loadedKind = kind;

        var samples = points
            .Select(point => (
                X: point.Time - context.OriginSeconds,
                Y: GetSignalValue(point, kind),
                Point: point))
            .Where(sample => double.IsFinite(sample.X)
                             && sample.Y is { } y
                             && double.IsFinite(y))
            .Select(sample => new TrackSignalSample(sample.X, sample.Y!.Value, sample.Point))
            .ToArray();
        cursorSamples.AddRange(samples);

        var signalColor = Color.FromHex("#ffffbf");
        var (label, unit, format) = kind switch
        {
            TrackSignalKind.Speed => ("Speed", "km/h", "0.#"),
            TrackSignalKind.Elevation => ("Elevation", "m", "0.#"),
            _ => ("Track", string.Empty, "0.##")
        };
        var title = kind switch
        {
            TrackSignalKind.Speed => "Speed (km/h)",
            TrackSignalKind.Elevation => "Elevation (m)",
            _ => "Value"
        };
        var message = kind switch
        {
            TrackSignalKind.Speed => "No speed data",
            TrackSignalKind.Elevation => "No elevation data",
            _ => "No track data"
        };

        IReadOnlyList<RecordedTimeSeries> series = samples.Length < 2
            ? []
            :
            [
                new RecordedTimeSeries(
                    label,
                    unit,
                    signalColor,
                    new ExplicitValues(
                        samples.Select(sample => sample.X).ToArray(),
                        samples.Select(sample => sample.Y).ToArray()),
                    format)
            ];

        LoadTimeSeries(new RecordedTimeSeriesData(
            title,
            message,
            context.DurationSeconds,
            series,
            MarkerSource: telemetryData));
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

    protected override CursorReadout? GetCursorReadout(double position)
    {
        var readout = base.GetCursorReadout(position);
        if (readout is null || loadedKind != TrackSignalKind.Elevation)
        {
            return readout;
        }

        var qualityPoint = GetNearestQualityCursorSample(position)?.Point;
        if (qualityPoint is null)
        {
            return readout;
        }

        var lines = new List<CursorReadoutLine>(readout.Lines);
        if (qualityPoint.Satellites is { } satellites)
        {
            lines.Add(new CursorReadoutLine("Satellites", satellites, string.Empty, gpsQualityReadoutColor, "0"));
        }

        if (qualityPoint.Epe2d is { } epe2d)
        {
            lines.Add(new CursorReadoutLine("EPE 2D", epe2d, "m", gpsQualityReadoutColor, "0.##"));
        }

        if (qualityPoint.Epe3d is { } epe3d)
        {
            lines.Add(new CursorReadoutLine("EPE 3D", epe3d, "m", gpsQualityReadoutColor, "0.##"));
        }

        return lines.Count == readout.Lines.Count
            ? readout
            : readout with { Lines = lines };
    }

    private TrackSignalSample? GetNearestQualityCursorSample(double position)
    {
        if (!double.IsFinite(position) || cursorSamples.Count == 0)
        {
            return null;
        }

        TrackSignalSample? nearestSample = null;
        var nearestDistance = double.PositiveInfinity;
        foreach (var sample in cursorSamples)
        {
            if (!HasGpsQuality(sample.Point))
            {
                continue;
            }

            var distance = Math.Abs(position - sample.X);
            if (distance < nearestDistance)
            {
                nearestSample = sample;
                nearestDistance = distance;
            }
        }

        return nearestSample;
    }

    private static bool HasGpsQuality(TrackPoint point)
    {
        return point.Satellites is not null ||
               point.Epe2d is not null ||
               point.Epe3d is not null;
    }

    private readonly record struct TrackSignalSample(double X, double Y, TrackPoint Point);
}

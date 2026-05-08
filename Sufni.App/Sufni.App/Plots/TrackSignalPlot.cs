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
    public void LoadTrackData(
        IReadOnlyList<TrackPoint> points,
        TrackTimeRange context,
        TelemetryData? telemetryData,
        TrackSignalKind kind)
    {
        var samples = points
            .Select(point => (X: point.Time - context.OriginSeconds, Y: GetSignalValue(point, kind)))
            .Where(sample => double.IsFinite(sample.X)
                             && sample.Y is { } y
                             && double.IsFinite(y))
            .Select(sample => (sample.X, Y: sample.Y!.Value))
            .ToArray();

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
}

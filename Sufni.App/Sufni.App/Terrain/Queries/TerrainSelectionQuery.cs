using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Queries;

public sealed record TerrainSelectionResult(TelemetryTimeSelection? Selection, double SelectedMetres,
    ImmutableArray<double> RoughnessMetres, ImmutableArray<double> SteepnessMetres);

public static class TerrainSelectionQuery
{
    public static TerrainSelectionResult Evaluate(TerrainProfile profile, TerrainClassFilter filter, TelemetryTimeRange? range)
    {
        var roughness = new double[3];
        var steepness = new double[3];
        var intervals = new List<TelemetryTimeRange>();
        double start = 0, end = 0, distance = 0, selected = 0;
        foreach (var window in profile.Windows)
        {
            if (!window.Moving) continue;
            var from = Math.Max(window.StartSeconds, range?.StartSeconds ?? window.StartSeconds);
            var to = Math.Min(window.EndSeconds, range?.EndSeconds ?? window.EndSeconds);
            if (to <= from) continue;
            var metres = window.MeanSpeed * (to - from);
            if (window.Roughness is { } r) roughness[(int)r] += metres;
            if (window.Steepness is { } s) steepness[(int)s] += metres;
            if (!filter.Matches(window)) continue;
            if (distance == 0 || from != end)
            {
                FinishInterval();
                start = from;
            }
            end = to;
            distance += metres;
        }
        FinishInterval();
        return new(filter.IsEmpty ? (TelemetryTimeSelection?)range : new(intervals), selected, [.. roughness], [.. steepness]);

        void FinishInterval()
        {
            if (distance > 0 && TelemetryTimeRange.TryCreate(start, end, out var interval))
            {
                intervals.Add(interval);
                selected += distance;
            }
            distance = 0;
        }
    }
}

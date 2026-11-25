using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Queries;

/// <summary>The scoped windows inside the analysis range, with the moving distance of each class there.</summary>
public sealed record TerrainSelectionResult(TelemetryTimeSelection Selection, double SelectedMetres, double StoppedSeconds,
    ImmutableArray<double> RoughnessMetres, ImmutableArray<double> GradeMetres, double MovingMetres)
{
    public double PresetMetres(TerrainPreset preset)
    {
        var climb = Enum.GetValues<GradeBand>().Where(band => band.IsClimb()).Sum(band => GradeMetres[(int)band]);
        return preset switch
        {
            TerrainPreset.Descents => MovingMetres - climb,
            TerrainPreset.Climbs => climb,
            _ => MovingMetres
        };
    }
}

public static class TerrainSelectionQuery
{
    public static TerrainSelectionResult Evaluate(ClassifiedProfile profile, TerrainScope scope, TelemetryTimeRange? range)
    {
        var roughness = new double[Enum.GetValues<RoughnessClass>().Length];
        var grade = new double[Enum.GetValues<GradeBand>().Length];
        var intervals = new List<TelemetryTimeRange>();
        double start = 0, end = 0, distance = 0, selected = 0, stopped = 0, moving = 0;
        foreach (var classified in profile.Windows)
        {
            var window = classified.Window;
            var from = Math.Max(window.StartSeconds, range?.StartSeconds ?? window.StartSeconds);
            var to = Math.Min(window.EndSeconds, range?.EndSeconds ?? window.EndSeconds);
            if (to <= from) continue;
            if (!window.Moving)
            {
                stopped += to - from;
                continue;
            }
            var metres = window.MeanSpeed * (to - from);
            moving += metres;
            if (classified.Roughness is { } r) roughness[(int)r] += metres;
            if (window.Band is { } b) grade[(int)b] += metres;
            if (!scope.Matches(classified)) continue;
            if (distance == 0 || from != end)
            {
                FinishInterval();
                start = from;
            }
            end = to;
            distance += metres;
        }
        FinishInterval();
        return new(new(intervals), selected, stopped, [.. roughness], [.. grade], moving);

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

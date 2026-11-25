using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Queries;

public static class SetupTerrainScaleQuery
{
    public const int MinimumRides = 3;
    public const double MinimumMetres = 5000;

    /// <summary>
    /// One scale per setup among the sessions. A session contributes when its terrain profile is current; a sensor
    /// is ready once its contributing rides reach <see cref="MinimumRides"/> and <see cref="MinimumMetres"/>.
    /// </summary>
    public static IReadOnlyDictionary<Guid, SetupTerrainScale> Evaluate(IEnumerable<SessionSnapshot> sessions,
        IReadOnlyList<TrackSnapshot> tracks)
    {
        var contributions = new Dictionary<Guid, List<TerrainSummary>>();
        foreach (var session in sessions)
        {
            if (session.SetupId is not { } setupId) continue;
            if (!contributions.TryGetValue(setupId, out var summaries)) contributions[setupId] = summaries = [];
            if (SessionTerrainStatusQuery.Evaluate(session, tracks).Status is TerrainStatus.Current current)
                summaries.Add(current.Summary);
        }
        return contributions.ToDictionary(pair => pair.Key, pair => new SetupTerrainScale(
            Scale(pair.Value.Select(summary => summary.FrameImu)),
            Scale(pair.Value.Select(summary => summary.ForkImu)),
            Scale(pair.Value.Select(summary => summary.RearAxleImu)),
            Scale(pair.Value.Select(summary => summary.FrameClimb))));
    }

    /// <summary>The scale key that the session summaries carry for each setup.</summary>
    public static TerrainScaleCatalog Catalog(IEnumerable<RecordedSessionSummary> sessions)
    {
        var keys = ImmutableDictionary.CreateBuilder<Guid, TerrainScaleKey>();
        foreach (var session in sessions)
            if (session.SetupId is { } setupId && session.TerrainScale is { } key) keys[setupId] = key;
        return new(keys.ToImmutable());
    }

    private static SensorScale Scale(IEnumerable<LevelDistribution?> rides)
    {
        var merged = new SortedDictionary<int, double>();
        var count = 0;
        foreach (var ride in rides)
        {
            if (ride is null) continue;
            count++;
            foreach (var (bin, metres) in ride.MetresByBin) merged[bin] = merged.GetValueOrDefault(bin) + metres;
        }
        var total = merged.Values.Sum();
        if (count < MinimumRides || total < MinimumMetres) return new SensorScale.NotEnoughData(count, total);
        var cumulative = 0d;
        foreach (var (bin, metres) in merged)
        {
            cumulative += metres;
            if (cumulative >= total / 2) return new SensorScale.Ready(bin);
        }
        throw new UnreachableException();
    }
}

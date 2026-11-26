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

    /// <summary>A chosen limit, not a measured one: the labelled distance at which labels set the median of a sensor.</summary>
    public const double MinimumLabelledMetres = 100;

    /// <summary>
    /// One scale per setup among the sessions. A session contributes when its terrain profile is current; a sensor
    /// is ready once its contributing rides reach <see cref="MinimumRides"/> and <see cref="MinimumMetres"/>. Labelled
    /// readings of current label summaries anchor a sensor at <see cref="MinimumLabelledMetres"/>; the climb limit needs
    /// that distance in each climb class. A session that is not in <paramref name="labels"/> has no labels.
    /// </summary>
    public static IReadOnlyDictionary<Guid, SetupTerrainScale> Evaluate(IEnumerable<SessionSnapshot> sessions,
        IReadOnlyList<TrackSnapshot> tracks, IReadOnlyDictionary<Guid, TerrainLabelSet> labels)
    {
        var contributions = new Dictionary<Guid, (List<TerrainSummary> Rides, List<TerrainLabelSummary> Labels)>();
        foreach (var session in sessions)
        {
            if (session.SetupId is not { } setupId) continue;
            if (!contributions.TryGetValue(setupId, out var contribution)) contributions[setupId] = contribution = ([], []);
            if (SessionTerrainStatusQuery.Evaluate(session, tracks).Status is not TerrainStatus.Current current) continue;
            contribution.Rides.Add(current.Summary);
            if (SessionTerrainStatusQuery.EvaluateLabels(session, current, labels.GetValueOrDefault(session.Id) ?? TerrainLabelSet.None)
                is TerrainLabelState.Current labelled)
                contribution.Labels.Add(labelled.Summary);
        }
        return contributions.ToDictionary(pair => pair.Key, pair =>
        {
            var (rides, labelled) = pair.Value;
            var frame = Merge(labelled.Select(summary => summary.FrameImu));
            var fork = Merge(labelled.Select(summary => summary.ForkImu));
            var rearAxle = Merge(labelled.Select(summary => summary.RearAxleImu));
            var smoothClimb = Merge(labelled.Select(summary => summary.SmoothClimb));
            var technicalClimb = Merge(labelled.Select(summary => summary.TechnicalClimb));
            return new SetupTerrainScale(
                Anchor(frame) ?? Scale(rides.Select(summary => summary.FrameImu)),
                Anchor(fork) ?? Scale(rides.Select(summary => summary.ForkImu)),
                Anchor(rearAxle) ?? Scale(rides.Select(summary => summary.RearAxleImu)),
                ClimbAnchor(smoothClimb, technicalClimb) ?? Scale(rides.Select(summary => summary.FrameClimb)),
                new TerrainLabelCoverage(Metres(frame), Metres(fork), Metres(rearAxle), Metres(smoothClimb), Metres(technicalClimb)));
        });
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
        var count = rides.Count(ride => ride is not null);
        var merged = Merge(rides);
        var total = Metres(merged);
        return count < MinimumRides || total < MinimumMetres
            ? new SensorScale.NotEnoughData(count, total)
            : new SensorScale.Ready(Median(merged));
    }

    private static SensorScale.Anchored? Anchor(SortedDictionary<int, double> labelled) =>
        Metres(labelled) >= MinimumLabelledMetres ? new(Median(labelled)) : null;

    private static SensorScale.Anchored? ClimbAnchor(SortedDictionary<int, double> smooth, SortedDictionary<int, double> technical) =>
        Metres(smooth) >= MinimumLabelledMetres && Metres(technical) >= MinimumLabelledMetres
            ? new((int)Math.Floor((Median(smooth) + Median(technical)) / 2.0))
            : null;

    private static SortedDictionary<int, double> Merge(IEnumerable<LevelDistribution?> distributions)
    {
        var merged = new SortedDictionary<int, double>();
        foreach (var distribution in distributions)
        {
            if (distribution is null) continue;
            foreach (var (bin, metres) in distribution.MetresByBin) merged[bin] = merged.GetValueOrDefault(bin) + metres;
        }
        return merged;
    }

    private static double Metres(SortedDictionary<int, double> bins) => bins.Values.Sum();

    /// <summary>The first bin at which the cumulative distance reaches half of the total.</summary>
    private static int Median(SortedDictionary<int, double> bins)
    {
        var total = Metres(bins);
        var cumulative = 0d;
        foreach (var (bin, metres) in bins)
        {
            cumulative += metres;
            if (cumulative >= total / 2) return bin;
        }
        throw new UnreachableException();
    }
}

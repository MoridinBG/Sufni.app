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
        var contributions = new Dictionary<Guid, (List<TerrainSummary> Rides, List<(Guid SessionId, LabelledStretch Stretch)> Stretches)>();
        foreach (var session in sessions)
        {
            if (session.SetupId is not { } setupId) continue;
            if (!contributions.TryGetValue(setupId, out var contribution)) contributions[setupId] = contribution = ([], []);
            if (SessionTerrainStatusQuery.Evaluate(session, tracks).Status is not TerrainStatus.Current current) continue;
            contribution.Rides.Add(current.Summary);
            if (SessionTerrainStatusQuery.EvaluateLabels(session, current, labels.GetValueOrDefault(session.Id) ?? TerrainLabelSet.None)
                is TerrainLabelState.Current labelled)
                contribution.Stretches.AddRange(labelled.Summary.Stretches.Select(stretch => (session.Id, stretch)));
        }
        return contributions.ToDictionary(pair => pair.Key, pair =>
        {
            var (rides, stretches) = pair.Value;
            var fromRides = new SetupTerrainScale(Scale(rides.Select(summary => summary.FrameImu)),
                Scale(rides.Select(summary => summary.ForkImu)), Scale(rides.Select(summary => summary.RearAxleImu)),
                Scale(rides.Select(summary => summary.FrameClimb)), TerrainLabelCoverage.None);
            var readings = ImmutableDictionary.CreateBuilder<(Guid SessionId, TerrainLabelRange Label), LabelReading>();
            for (var i = 0; i < stretches.Count; i++)
            {
                var others = WithLabels(fromRides, stretches.Where((_, j) => j != i).Select(entry => entry.Stretch));
                if (TerrainClassifier.Read(stretches[i].Stretch, others.Key) is { } reading)
                    readings[(stretches[i].SessionId, stretches[i].Stretch.Label)] = reading;
            }
            return WithLabels(fromRides, stretches.Select(entry => entry.Stretch)) with { Readings = readings.ToImmutable() };
        });
    }

    private static SetupTerrainScale WithLabels(SetupTerrainScale fromRides, IEnumerable<LabelledStretch> labelled)
    {
        var stretches = labelled.ToArray();
        var descents = stretches.Where(stretch => !stretch.Label.Class.IsClimb()).ToArray();
        var frame = Levels(descents, stretch => stretch.FrameImu);
        var fork = Levels(descents, stretch => stretch.ForkImu);
        var rearAxle = Levels(descents, stretch => stretch.RearAxleImu);
        var smoothClimb = Levels(stretches.Where(stretch => stretch.Label.Class == RoughnessClass.SmoothClimb), stretch => stretch.FrameImu);
        var technicalClimb = Levels(stretches.Where(stretch => stretch.Label.Class == RoughnessClass.TechnicalClimb), stretch => stretch.FrameImu);
        return new SetupTerrainScale(Anchor(frame) ?? fromRides.FrameImu, Anchor(fork) ?? fromRides.ForkImu,
            Anchor(rearAxle) ?? fromRides.RearAxleImu, ClimbAnchor(smoothClimb, technicalClimb) ?? fromRides.FrameClimb,
            new TerrainLabelCoverage(Metres(frame), Metres(fork), Metres(rearAxle), Metres(smoothClimb), Metres(technicalClimb)));
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

    private static (RoughnessClass Class, StretchLevel Level)[] Levels(IEnumerable<LabelledStretch> stretches,
        Func<LabelledStretch, StretchLevel?> sensor) =>
        [.. stretches.Where(stretch => sensor(stretch) is not null).Select(stretch => (stretch.Label.Class, sensor(stretch)!))];

    private static double Metres((RoughnessClass Class, StretchLevel Level)[] levels) => levels.Sum(level => level.Level.Metres);

    private static SensorScale.Anchored? Anchor((RoughnessClass Class, StretchLevel Level)[] labelled) =>
        Metres(labelled) >= MinimumLabelledMetres
            ? new(TerrainLabels.Anchor([.. labelled.Select(label => (label.Level.Level, label.Class))]))
            : null;

    private static SensorScale.Anchored? ClimbAnchor((RoughnessClass Class, StretchLevel Level)[] smooth,
        (RoughnessClass Class, StretchLevel Level)[] technical) =>
        Metres(smooth) >= MinimumLabelledMetres && Metres(technical) >= MinimumLabelledMetres
            ? new(TerrainLabels.ClimbAnchor([.. smooth.Select(label => label.Level.Level)], [.. technical.Select(label => label.Level.Level)]))
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

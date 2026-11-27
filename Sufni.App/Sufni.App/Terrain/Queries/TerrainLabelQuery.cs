using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Stores;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Stores;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Queries;

/// <summary>A label of a setup. <see cref="Reading"/> is how its stretch reads on the setup scale, null until known.</summary>
public sealed record SetupTerrainLabelRow(Guid LabelId, Guid SessionId, string SessionName, long Timestamp, RoughnessClass Class,
    TelemetryTimeRange Range, LabelReading? Reading = null);

/// <summary>The scale of a setup, null without a session, and the labels inside the windows of its sessions.</summary>
public sealed record SetupTerrainLabels(SetupTerrainScale? Scale, ImmutableArray<SetupTerrainLabelRow> Labels);

/// <summary>The terrain labels of sessions. Labels belong to a recording source; a session holds those inside its window.</summary>
public interface ITerrainLabelQuery
{
    TerrainLabelSet LabelsOf(SessionSnapshot session);

    IReadOnlyDictionary<Guid, TerrainLabelSet> LabelsOf(IEnumerable<SessionSnapshot> sessions);

    /// <summary>The sessions whose recording source has the given owner key.</summary>
    IReadOnlyList<Guid> SessionsOf(string ownerKey);

    /// <summary>The label rows of the sessions of a setup, newest session first and then in order of start.</summary>
    SetupTerrainLabels ForSetup(Guid setupId);

    /// <summary>Emits after each publication that changes a session, a source, a track or a label.</summary>
    IObservable<Unit> Changes { get; }
}

internal sealed class TerrainLabelQuery(
    ISessionStore sessionStore,
    IRecordedSessionSourceStore sources,
    ITerrainLabelStore labels,
    ITrackStore tracks,
    StorePublication publication) : ITerrainLabelQuery
{
    public IObservable<Unit> Changes { get; } = publication.Committed
        .Where(commit => commit.Stores.Contains(sessionStore) || commit.Stores.Contains(sources) ||
                         commit.Stores.Contains(tracks) || commit.Stores.Contains(labels))
        .Select(_ => Unit.Default);

    /// <summary>
    /// The labels that the session window holds fully, in telemetry seconds, in order of start and then of ID.
    /// </summary>
    public static IReadOnlyList<(Guid Id, TelemetryTimeRange Range, RoughnessClass Class)> InWindow(
        IEnumerable<TerrainLabelSnapshot> ownerLabels, double windowStartSeconds, double durationSeconds)
    {
        var timeline = new SessionTrackTimeline(0, OwnerOriginSeconds: windowStartSeconds);
        var result = new List<(Guid Id, TelemetryTimeRange Range, RoughnessClass Class)>();
        foreach (var label in ownerLabels.OrderBy(l => l.StartSeconds).ThenBy(l => l.Id))
        {
            if (timeline.ContainsOwnerRange(label.StartSeconds, label.EndSeconds, durationSeconds) &&
                TelemetryTimeRange.TryCreate(timeline.OwnerToTelemetrySeconds(label.StartSeconds),
                    timeline.OwnerToTelemetrySeconds(label.EndSeconds), out var range))
                result.Add((label.Id, range, label.Class));
        }
        return result;
    }

    public TerrainLabelSet LabelsOf(SessionSnapshot session) =>
        new([.. LabelsInWindow(session).Select(l => new TerrainLabelRange(l.Range.StartSeconds, l.Range.EndSeconds, l.Class))]);

    public IReadOnlyDictionary<Guid, TerrainLabelSet> LabelsOf(IEnumerable<SessionSnapshot> sessions) =>
        sessions.ToDictionary(s => s.Id, LabelsOf);

    public IReadOnlyList<Guid> SessionsOf(string ownerKey) =>
        [.. sessionStore.GetAll().Where(s => OwnerKey(s) == ownerKey).Select(s => s.Id)];

    public SetupTerrainLabels ForSetup(Guid setupId)
    {
        var sessions = sessionStore.GetAll().Where(s => s.SetupId == setupId).ToArray();
        if (sessions.Length == 0) return new(null, []);
        var trackSnapshots = tracks.GetAll();
        var labelSets = LabelsOf(sessions);
        var scale = SetupTerrainScaleQuery.Evaluate(sessions, trackSnapshots, labelSets).GetValueOrDefault(setupId);
        return new(scale, [.. sessions.OrderByDescending(s => s.Timestamp).SelectMany(session =>
        {
            var stretches = CurrentStretches(session, trackSnapshots, labelSets[session.Id]);
            return LabelsInWindow(session).Select(l => new SetupTerrainLabelRow(l.Id, session.Id, session.Name, session.Timestamp ?? 0,
                l.Class, l.Range, ReadingOf(stretches, new TerrainLabelRange(l.Range.StartSeconds, l.Range.EndSeconds, l.Class), scale)));
        })]);
    }

    private static ImmutableArray<LabelledStretch> CurrentStretches(SessionSnapshot session, IReadOnlyList<TrackSnapshot> trackSnapshots,
        TerrainLabelSet labels) =>
        SessionTerrainStatusQuery.Evaluate(session, trackSnapshots).Status is TerrainStatus.Current current &&
        SessionTerrainStatusQuery.EvaluateLabels(session, current, labels) is TerrainLabelState.Current labelled
            ? labelled.Summary.Stretches
            : [];

    private static LabelReading? ReadingOf(ImmutableArray<LabelledStretch> stretches, TerrainLabelRange label, SetupTerrainScale? scale) =>
        scale is not null && stretches.FirstOrDefault(stretch => stretch.Label == label) is { } stretch
            ? TerrainClassifier.Read(stretch, scale.Key)
            : null;

    private IReadOnlyList<(Guid Id, TelemetryTimeRange Range, RoughnessClass Class)> LabelsInWindow(SessionSnapshot session) =>
        session.DurationSeconds is > 0 && OwnerKey(session) is { } ownerKey
            ? InWindow(labels.GetForOwner(ownerKey), session.WindowStartSeconds, session.DurationSeconds.Value)
            : [];

    private string? OwnerKey(SessionSnapshot session) => sources.Get(session.RecordingSourceSessionId)?.SourceHash;
}

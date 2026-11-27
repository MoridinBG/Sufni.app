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

/// <summary>A roughness label of a setup. <see cref="Reading"/> is how its stretch reads on the scale of everything else, null until known.</summary>
public sealed record SetupTerrainLabelRow(Guid LabelId, Guid SessionId, string SessionName, long Timestamp, RoughnessClass Class,
    TelemetryTimeRange Range, LabelReading? Reading = null);

/// <summary>A steepness label of a setup. <see cref="Reading"/> is how its stretch reads, null until known.</summary>
public sealed record SetupSteepnessLabelRow(Guid LabelId, Guid SessionId, string SessionName, long Timestamp, GradeBand Band,
    TelemetryTimeRange Range, GradeReading? Reading = null);

/// <summary>The scale of a setup, null without a session, and the labels of each kind inside the windows of its sessions.</summary>
public sealed record SetupTerrainLabels(SetupTerrainScale? Scale, ImmutableArray<SetupTerrainLabelRow> Labels)
{
    public ImmutableArray<SetupSteepnessLabelRow> SteepnessLabels { get; init; } = [];
}

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
    public static IReadOnlyList<(Guid Id, TelemetryTimeRange Range, TerrainLabelClass Class)> InWindow(
        IEnumerable<TerrainLabelSnapshot> ownerLabels, double windowStartSeconds, double durationSeconds)
    {
        var timeline = new SessionTrackTimeline(0, OwnerOriginSeconds: windowStartSeconds);
        var result = new List<(Guid Id, TelemetryTimeRange Range, TerrainLabelClass Class)>();
        foreach (var label in ownerLabels.OrderBy(l => l.StartSeconds).ThenBy(l => l.Id))
        {
            if (timeline.ContainsOwnerRange(label.StartSeconds, label.EndSeconds, durationSeconds) &&
                TelemetryTimeRange.TryCreate(timeline.OwnerToTelemetrySeconds(label.StartSeconds),
                    timeline.OwnerToTelemetrySeconds(label.EndSeconds), out var range))
                result.Add((label.Id, range, label.Class));
        }
        return result;
    }

    public TerrainLabelSet LabelsOf(SessionSnapshot session)
    {
        var inWindow = LabelsInWindow(session);
        return new([.. inWindow.Select(l => l.Class is TerrainLabelClass.Roughness roughness
                ? new TerrainLabelRange(l.Range.StartSeconds, l.Range.EndSeconds, roughness.Class) : null).OfType<TerrainLabelRange>()])
        {
            Grades = [.. inWindow.Select(l => l.Class is TerrainLabelClass.Steepness steepness
                ? new GradeLabelRange(l.Range.StartSeconds, l.Range.EndSeconds, steepness.Band) : null).OfType<GradeLabelRange>()]
        };
    }

    public IReadOnlyDictionary<Guid, TerrainLabelSet> LabelsOf(IEnumerable<SessionSnapshot> sessions) =>
        sessions.ToDictionary(s => s.Id, LabelsOf);

    public IReadOnlyList<Guid> SessionsOf(string ownerKey) =>
        [.. sessionStore.GetAll().Where(s => OwnerKey(s) == ownerKey).Select(s => s.Id)];

    public SetupTerrainLabels ForSetup(Guid setupId)
    {
        var sessions = sessionStore.GetAll().Where(s => s.SetupId == setupId).OrderByDescending(s => s.Timestamp).ToArray();
        if (sessions.Length == 0) return new(null, []);
        var trackSnapshots = tracks.GetAll();
        var labelSets = LabelsOf(sessions);
        var scale = SetupTerrainScaleQuery.Evaluate(sessions, trackSnapshots, labelSets).GetValueOrDefault(setupId);
        return new(scale, [.. sessions.SelectMany(session => LabelsInWindow(session).Select(l => l.Class is TerrainLabelClass.Roughness roughness
            ? new SetupTerrainLabelRow(l.Id, session.Id, session.Name, session.Timestamp ?? 0, roughness.Class, l.Range,
                scale?.ReadingOf(session.Id, new TerrainLabelRange(l.Range.StartSeconds, l.Range.EndSeconds, roughness.Class)))
            : null).OfType<SetupTerrainLabelRow>())])
        {
            SteepnessLabels = [.. sessions.SelectMany(session =>
            {
                var summary = SessionTerrainStatusQuery.EvaluateLabels(session,
                    SessionTerrainStatusQuery.Evaluate(session, trackSnapshots).Status, labelSets[session.Id]) is TerrainLabelState.Current current
                    ? current.Summary
                    : null;
                return LabelsInWindow(session).Select(l => l.Class is TerrainLabelClass.Steepness steepness
                    ? new SetupSteepnessLabelRow(l.Id, session.Id, session.Name, session.Timestamp ?? 0, steepness.Band, l.Range,
                        summary?.ReadingOf(new GradeLabelRange(l.Range.StartSeconds, l.Range.EndSeconds, steepness.Band)))
                    : null).OfType<SetupSteepnessLabelRow>();
            })]
        };
    }

    private IReadOnlyList<(Guid Id, TelemetryTimeRange Range, TerrainLabelClass Class)> LabelsInWindow(SessionSnapshot session) =>
        session.DurationSeconds is > 0 && OwnerKey(session) is { } ownerKey
            ? InWindow(labels.GetForOwner(ownerKey), session.WindowStartSeconds, session.DurationSeconds.Value)
            : [];

    private string? OwnerKey(SessionSnapshot session) => sources.Get(session.RecordingSourceSessionId)?.SourceHash;
}

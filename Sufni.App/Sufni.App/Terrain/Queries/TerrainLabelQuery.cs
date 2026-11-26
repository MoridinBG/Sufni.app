using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Terrain.Stores;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Queries;

/// <summary>The terrain labels of sessions. Labels belong to a recording source; a session holds those inside its window.</summary>
public interface ITerrainLabelQuery
{
    TerrainLabelSet LabelsOf(SessionSnapshot session);

    IReadOnlyDictionary<Guid, TerrainLabelSet> LabelsOf(IEnumerable<SessionSnapshot> sessions);

    /// <summary>The sessions whose recording source has the given owner key.</summary>
    IReadOnlyList<Guid> SessionsOf(string ownerKey);
}

internal sealed class TerrainLabelQuery(
    ISessionStore sessionStore,
    IRecordedSessionSourceStore sources,
    ITerrainLabelStore labels) : ITerrainLabelQuery
{
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

    public TerrainLabelSet LabelsOf(SessionSnapshot session)
    {
        if (session.DurationSeconds is not > 0 || OwnerKey(session) is not { } ownerKey) return TerrainLabelSet.None;
        return new([.. InWindow(labels.GetForOwner(ownerKey), session.WindowStartSeconds, session.DurationSeconds.Value)
            .Select(l => new TerrainLabelRange(l.Range.StartSeconds, l.Range.EndSeconds, l.Class))]);
    }

    public IReadOnlyDictionary<Guid, TerrainLabelSet> LabelsOf(IEnumerable<SessionSnapshot> sessions) =>
        sessions.ToDictionary(s => s.Id, LabelsOf);

    public IReadOnlyList<Guid> SessionsOf(string ownerKey) =>
        [.. sessionStore.GetAll().Where(s => OwnerKey(s) == ownerKey).Select(s => s.Id)];

    private string? OwnerKey(SessionSnapshot session) => sources.Get(session.RecordingSourceSessionId)?.SourceHash;
}

using System.Collections.Generic;
using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Queries;

public static class SessionTerrainStatusQuery
{
    public static (TerrainProfileKey Key, TerrainStatus Status) Evaluate(SessionSnapshot session,
        IReadOnlyList<TrackSnapshot> tracks) => Evaluate(session, tracks, out _);

    internal static (TerrainProfileKey Key, TerrainStatus Status) Evaluate(SessionSnapshot session,
        IReadOnlyList<TrackSnapshot> tracks, out TrackSnapshot? selected)
    {
        var key = TerrainProfileKey.From(SessionContentToken.Create(session, tracks, out selected));
        if (!session.HasProcessedData)
            return (key, new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoTelemetry));
        if (session.TerrainProfileKey != key)
            return (key, new TerrainStatus.Pending());

        TerrainStatus status = session.TerrainOutcome switch
        {
            TerrainOutcome.Computed when session.TerrainSummary is { } summary => new TerrainStatus.Current(key, summary),
            TerrainOutcome.NoTrack => new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoTrack),
            TerrainOutcome.NoMovement => new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoMovement),
            TerrainOutcome.ContentMissing => new TerrainStatus.Unavailable(TerrainAvailabilityReason.ContentMissing),
            TerrainOutcome.Failed => new TerrainStatus.Failed(),
            _ => new TerrainStatus.Pending()
        };
        return (key, status);
    }

    /// <summary>The label summary state of a session with the given profile status and its labels.</summary>
    public static TerrainLabelState EvaluateLabels(SessionSnapshot session, TerrainStatus status, TerrainLabelSet labels)
    {
        if (status is not TerrainStatus.Current current || labels.IsEmpty) return new TerrainLabelState.None();
        return session.TerrainLabelKey == new TerrainLabelKey(current.Key, labels, TerrainLabels.Version) &&
               session.TerrainLabelSummary is { } summary
            ? new TerrainLabelState.Current(summary)
            : new TerrainLabelState.Pending();
    }

    /// <summary>The class state of a session with the given profile status on the scale of its setup and with its labels.</summary>
    public static TerrainClassState EvaluateClasses(SessionSnapshot session, TerrainStatus status, TerrainScaleKey? scale,
        TerrainLabelSet labels)
    {
        if (status is not TerrainStatus.Current current || scale is not { } known ||
            known == TerrainScaleKey.None && labels.Ranges.IsEmpty)
            return new TerrainClassState.Unavailable();
        return session.TerrainClassKey == new TerrainClassKey(current.Key, known, labels, TerrainClassifier.Version) &&
               session.TerrainClassSummary is { } summary
            ? new TerrainClassState.Classified(summary)
            : new TerrainClassState.Pending();
    }
}

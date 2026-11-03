using System.Collections.Concurrent;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Doubles;

/// <summary>Session catalog and explicit snapshot inputs for segment workflows.</summary>
internal sealed class TestSessionContentReader : ISessionContentReader
{
    private readonly ConcurrentDictionary<Guid, int> trackReads = new();

    public IReadOnlyList<SessionCatalogItem> Sessions { get; set; } = [];
    public Dictionary<Guid, IReadOnlyList<TrackPoint>> Tracks { get; } = [];
    public Dictionary<Guid, TelemetryData> Telemetry { get; } = [];
    public Dictionary<Guid, SessionContentSnapshotResult> ExactResults { get; } = [];
    public Func<Guid, CancellationToken, Task<IReadOnlyList<TrackPoint>?>>? TrackLoader { get; set; }
    public int GetSessionsCalls { get; private set; }

    public int TrackReads(Guid sessionId) => trackReads.GetValueOrDefault(sessionId);

    public Task<IReadOnlyList<SessionCatalogItem>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        GetSessionsCalls++;
        return Task.FromResult(Sessions);
    }

    public Task<SessionCatalogItem?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Sessions.FirstOrDefault(session => session.Id == sessionId));

    public async Task<SessionContentSnapshotResult> GetExactSnapshotAsync(
        Guid sessionId,
        SessionContentToken expectedToken,
        SessionContentSelection selection,
        CancellationToken cancellationToken = default)
    {
        if (ExactResults.TryGetValue(sessionId, out var result)) return result;

        TelemetryData? telemetry = null;
        if (selection.HasFlag(SessionContentSelection.ProcessedTelemetry) &&
            !Telemetry.TryGetValue(sessionId, out telemetry))
        {
            return new SessionContentSnapshotResult.Missing();
        }

        IReadOnlyList<TrackPoint>? track = null;
        if (selection.HasFlag(SessionContentSelection.Track))
        {
            trackReads.AddOrUpdate(sessionId, 1, (_, count) => count + 1);
            track = TrackLoader is null
                ? Tracks.GetValueOrDefault(sessionId)
                : await TrackLoader(sessionId, cancellationToken);
            if (track is null)
            {
                return new SessionContentSnapshotResult.Missing();
            }
        }

        return new SessionContentSnapshotResult.Available(
            new SessionContentSnapshot(expectedToken, selection, telemetry, track));
    }
}

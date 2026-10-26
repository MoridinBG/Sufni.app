using System.Collections.Concurrent;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.Telemetry;

namespace Sufni.App.Tests.TestSupport.Doubles;

/// <summary>
/// In-memory <see cref="ISessionContentReader"/>. Exact reads follow the real
/// contract: a token other than the session's current one is stale, an absent
/// session or selected payload is missing. <see cref="TrackLoader"/> replaces the
/// track lookup to control timing.
/// </summary>
internal sealed class TestSessionContentReader : ISessionContentReader
{
    private readonly ConcurrentDictionary<(Guid, SessionContentToken), int> trackReads = new();

    public IReadOnlyList<SessionCatalogItem> Sessions { get; set; } = [];
    public Dictionary<Guid, IReadOnlyList<TrackPoint>> Tracks { get; } = [];
    public Dictionary<Guid, TelemetryData> Telemetry { get; } = [];
    public Func<Guid, CancellationToken, Task<IReadOnlyList<TrackPoint>?>>? TrackLoader { get; set; }
    public int GetSessionsCalls { get; private set; }

    public int TrackReads(Guid sessionId) => trackReads.Where(item => item.Key.Item1 == sessionId).Sum(item => item.Value);

    public int TrackReads(Guid sessionId, SessionContentToken token) => trackReads.GetValueOrDefault((sessionId, token));

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
        var session = Sessions.FirstOrDefault(item => item.Id == sessionId);
        if (session is null)
        {
            return new SessionContentSnapshotResult.Missing();
        }

        if (session.ContentToken != expectedToken)
        {
            return new SessionContentSnapshotResult.Stale();
        }

        TelemetryData? telemetry = null;
        if (selection.HasFlag(SessionContentSelection.ProcessedTelemetry) &&
            !Telemetry.TryGetValue(sessionId, out telemetry))
        {
            return new SessionContentSnapshotResult.Missing();
        }

        IReadOnlyList<TrackPoint>? track = null;
        if (selection.HasFlag(SessionContentSelection.Track))
        {
            trackReads.AddOrUpdate((sessionId, expectedToken), 1, (_, count) => count + 1);
            track = TrackLoader is null
                ? Tracks.GetValueOrDefault(sessionId)
                : await TrackLoader(sessionId, cancellationToken);
            if (track is null)
            {
                return new SessionContentSnapshotResult.Missing();
            }
        }

        if (Sessions.FirstOrDefault(item => item.Id == sessionId)?.ContentToken != expectedToken)
        {
            return new SessionContentSnapshotResult.Stale();
        }

        return new SessionContentSnapshotResult.Available(
            new SessionContentSnapshot(expectedToken, selection, telemetry, track));
    }
}

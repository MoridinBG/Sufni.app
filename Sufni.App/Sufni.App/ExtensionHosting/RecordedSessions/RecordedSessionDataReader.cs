using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.ExtensionHosting.RecordedSessions;
using Sufni.App.ExtensionHost.RecordedSessions;

namespace Sufni.App.ExtensionHosting.RecordedSessions;

internal sealed class RecordedSessionDataReader(ISessionRepository sessionRepository) : IRecordedSessionDataReader
{
    public async Task<IReadOnlyList<RecordedSessionCatalogItem>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessions = await sessionRepository.GetSessionsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return sessions
            .Select(session => new RecordedSessionCatalogItem(
                session.Id,
                session.Name,
                session.Timestamp,
                session.DurationSeconds))
            .ToArray();
    }

    public async Task<RecordedSessionCatalogItem?> GetSessionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = await sessionRepository.GetSessionAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return session is null
            ? null
            : new RecordedSessionCatalogItem(
                session.Id,
                session.Name,
                session.Timestamp,
                session.DurationSeconds);
    }

    public async Task<TelemetryData?> GetProcessedTelemetryAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var telemetry = await sessionRepository.GetSessionPsstAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return telemetry;
    }

    public async Task<IReadOnlyList<TrackPoint>?> GetTrackAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var track = await sessionRepository.GetSessionTrackAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return track;
    }
}

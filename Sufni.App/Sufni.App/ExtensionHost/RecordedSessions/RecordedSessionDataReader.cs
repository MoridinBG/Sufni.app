using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.RecordedSessions;

internal sealed class RecordedSessionDataReader(IDatabaseService databaseService) : IRecordedSessionDataReader
{
    public async Task<IReadOnlyList<RecordedSessionCatalogItem>> GetSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sessions = await databaseService.GetSessionsAsync();
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
        var session = await databaseService.GetSessionAsync(sessionId);
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
        var telemetry = await databaseService.GetSessionPsstAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return telemetry;
    }

    public async Task<IReadOnlyList<TrackPoint>?> GetTrackAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var track = await databaseService.GetSessionTrackAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        return track;
    }
}

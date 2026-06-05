using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public sealed class RecordedSessionDataReader(IDatabaseService databaseService) : IRecordedSessionDataReader
{
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

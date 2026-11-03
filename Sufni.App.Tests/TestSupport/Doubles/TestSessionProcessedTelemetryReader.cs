using Sufni.Telemetry;

using Sufni.App.Sessions.Processing.Services;

namespace Sufni.App.Tests.TestSupport.Doubles;

internal sealed class TestSessionProcessedTelemetryReader : ISessionProcessedTelemetryReader
{
    private readonly Dictionary<Guid, TelemetryData?> telemetryBySession = [];

    public Exception? ReadFailure { get; set; }

    public List<Guid> RetainedSessionIds { get; } = [];

    public List<Guid> ReleasedSessionIds { get; } = [];

    public List<(Guid SessionId, long Revision)> ExactReads { get; } = [];

    public Func<Guid, long, CancellationToken, Task<TelemetryData?>>? ExactReader { get; set; }

    public void Set(Guid sessionId, TelemetryData? telemetry) =>
        telemetryBySession[sessionId] = telemetry;

    public IDisposable Retain(Guid sessionId)
    {
        RetainedSessionIds.Add(sessionId);
        return new Retention(() => ReleasedSessionIds.Add(sessionId));
    }

    public Task<TelemetryData?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (ReadFailure is { } failure) return Task.FromException<TelemetryData?>(failure);
        telemetryBySession.TryGetValue(sessionId, out var telemetry);
        return Task.FromResult(telemetry);
    }

    public Task<TelemetryData?> GetExactAsync(
        Guid sessionId,
        long processedTelemetryRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ExactReads.Add((sessionId, processedTelemetryRevision));
        return ExactReader?.Invoke(sessionId, processedTelemetryRevision, cancellationToken)
               ?? Task.FromResult<TelemetryData?>(null);
    }

    private sealed class Retention(Action release) : IDisposable
    {
        private Action? currentRelease = release;

        public void Dispose()
        {
            Interlocked.Exchange(ref currentRelease, null)?.Invoke();
        }
    }
}

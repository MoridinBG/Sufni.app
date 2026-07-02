using Sufni.Telemetry;

using Sufni.App.Sessions.Processing.Services;

namespace Sufni.App.Tests.TestSupport.Doubles;

internal sealed class TestSessionProcessedTelemetryReader : ISessionProcessedTelemetryReader
{
    private readonly Dictionary<Guid, TelemetryData?> telemetryBySession = [];
    private readonly Dictionary<Guid, int> getCallCounts = [];

    public List<Guid> RetainedSessionIds { get; } = [];

    public List<Guid> ReleasedSessionIds { get; } = [];

    public void Set(Guid sessionId, TelemetryData? telemetry) =>
        telemetryBySession[sessionId] = telemetry;

    public int GetCallCount(Guid sessionId) =>
        getCallCounts.GetValueOrDefault(sessionId);

    public IDisposable Retain(Guid sessionId)
    {
        RetainedSessionIds.Add(sessionId);
        return new Retention(() => ReleasedSessionIds.Add(sessionId));
    }

    public Task<TelemetryData?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        getCallCounts[sessionId] = GetCallCount(sessionId) + 1;
        telemetryBySession.TryGetValue(sessionId, out var telemetry);
        return Task.FromResult(telemetry);
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

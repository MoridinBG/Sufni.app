using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.Telemetry;

using Sufni.App.Sessions.Services;
namespace Sufni.App.Sessions.Processing.Services;

internal interface ISessionProcessedTelemetryReader
{
    /// <summary>
    /// Keeps the decoded telemetry for <paramref name="sessionId"/> available for
    /// reuse while a session editor is open. Returned <see cref="TelemetryData"/>
    /// instances from <see cref="GetAsync"/> may be shared and must be treated as
    /// read-only by consumers.
    /// </summary>
    IDisposable Retain(Guid sessionId);

    Task<TelemetryData?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<TelemetryData?> GetExactAsync(
        Guid sessionId,
        long processedTelemetryRevision,
        CancellationToken cancellationToken = default);
}

internal sealed class SessionProcessedTelemetryReader(
    ISessionRepository sessionRepository,
    ISessionTelemetryProcessor sessionTelemetryProcessor,
    IBackgroundTaskRunner backgroundTaskRunner) : ISessionProcessedTelemetryReader
{
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<Guid, RetainedTelemetry> retained = [];

    public IDisposable Retain(Guid sessionId)
    {
        lock (gate)
        {
            if (!retained.TryGetValue(sessionId, out var entry))
            {
                entry = new RetainedTelemetry();
                retained[sessionId] = entry;
            }

            entry.RetentionCount++;
        }

        return new Retention(this, sessionId);
    }

    public async Task<TelemetryData?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = await sessionRepository.GetSessionPsstPayloadMetadataAsync(sessionId);
            cancellationToken.ThrowIfCancellationRequested();
            if (metadata is null || !metadata.HasData)
            {
                ClearRetainedValue(sessionId);
                return null;
            }

            var key = new SessionPsstCacheKey(sessionId, metadata.ProcessedTelemetryRevision);
            var lazy = GetOrCreateLazy(sessionId, key);
            try
            {
                var telemetry = await lazy.Value.WaitAsync(cancellationToken);
                if (telemetry is not null || attempt == 1)
                {
                    if (telemetry is null)
                    {
                        ClearRetainedValue(sessionId, key, lazy);
                    }

                    return telemetry;
                }

                ClearRetainedValue(sessionId, key, lazy);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                ClearRetainedValue(sessionId, key, lazy);
                throw;
            }
        }

        return null;
    }

    public async Task<TelemetryData?> GetExactAsync(
        Guid sessionId,
        long processedTelemetryRevision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = new SessionPsstCacheKey(sessionId, processedTelemetryRevision);
        var lazy = GetOrCreateLazy(sessionId, key);
        try
        {
            var telemetry = await lazy.Value.WaitAsync(cancellationToken);
            if (telemetry is null)
            {
                ClearRetainedValue(sessionId, key, lazy);
            }

            return telemetry;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            ClearRetainedValue(sessionId, key, lazy);
            throw;
        }
    }

    private Lazy<Task<TelemetryData?>> GetOrCreateLazy(Guid sessionId, SessionPsstCacheKey key)
    {
        lock (gate)
        {
            if (!retained.TryGetValue(sessionId, out var entry))
            {
                return CreateLazy(key);
            }

            if (entry.Lazy is not null && entry.Key == key)
            {
                return entry.Lazy;
            }

            entry.Key = key;
            entry.Lazy = CreateLazy(key);
            return entry.Lazy;
        }
    }

    private Lazy<Task<TelemetryData?>> CreateLazy(SessionPsstCacheKey key) =>
        new(
            () => LoadAndDecodeAsync(key),
            LazyThreadSafetyMode.ExecutionAndPublication);

    private async Task<TelemetryData?> LoadAndDecodeAsync(SessionPsstCacheKey key)
    {
        var raw = await sessionRepository.GetSessionRawPsstAsync(key.SessionId, key.ProcessedTelemetryRevision);
        if (raw is null)
        {
            return null;
        }

        return await backgroundTaskRunner.RunAsync(
            () => sessionTelemetryProcessor.ReadProcessedTelemetryData(raw));
    }

    private void ClearRetainedValue(Guid sessionId)
    {
        lock (gate)
        {
            if (retained.TryGetValue(sessionId, out var entry))
            {
                entry.Key = null;
                entry.Lazy = null;
            }
        }
    }

    private void ClearRetainedValue(
        Guid sessionId,
        SessionPsstCacheKey key,
        Lazy<Task<TelemetryData?>> lazy)
    {
        lock (gate)
        {
            if (retained.TryGetValue(sessionId, out var entry) &&
                entry.Key == key &&
                ReferenceEquals(entry.Lazy, lazy))
            {
                entry.Key = null;
                entry.Lazy = null;
            }
        }
    }

    private void Release(Guid sessionId)
    {
        lock (gate)
        {
            if (!retained.TryGetValue(sessionId, out var entry))
            {
                return;
            }

            entry.RetentionCount--;
            if (entry.RetentionCount <= 0)
            {
                retained.Remove(sessionId);
            }
        }
    }

    private readonly record struct SessionPsstCacheKey(
        Guid SessionId,
        long ProcessedTelemetryRevision);

    private sealed class RetainedTelemetry
    {
        public int RetentionCount { get; set; }

        public SessionPsstCacheKey? Key { get; set; }

        public Lazy<Task<TelemetryData?>>? Lazy { get; set; }
    }

    private sealed class Retention(SessionProcessedTelemetryReader owner, Guid sessionId) : IDisposable
    {
        private SessionProcessedTelemetryReader? currentOwner = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref currentOwner, null)?.Release(sessionId);
        }
    }
}

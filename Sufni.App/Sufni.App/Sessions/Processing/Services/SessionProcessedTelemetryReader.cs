using System;
using System.Collections.Generic;
using System.Security.Cryptography;
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
        cancellationToken.ThrowIfCancellationRequested();
        var raw = await sessionRepository.GetSessionRawPsstAsync(sessionId);
        cancellationToken.ThrowIfCancellationRequested();
        if (raw is null)
        {
            ClearRetainedValue(sessionId);
            return null;
        }

        var hash = ComputeHash(raw);
        var lazy = GetOrCreateLazy(sessionId, hash, raw);
        try
        {
            return await backgroundTaskRunner.RunAsync(() => lazy.Value, cancellationToken);
        }
        catch
        {
            ClearRetainedValue(sessionId, hash, lazy);
            throw;
        }
    }

    private Lazy<TelemetryData> GetOrCreateLazy(Guid sessionId, string hash, byte[] raw)
    {
        lock (gate)
        {
            if (!retained.TryGetValue(sessionId, out var entry))
            {
                return CreateLazy(raw);
            }

            if (entry.Lazy is not null && StringComparer.Ordinal.Equals(entry.Hash, hash))
            {
                return entry.Lazy;
            }

            entry.Hash = hash;
            entry.Lazy = CreateLazy(raw);
            return entry.Lazy;
        }
    }

    private Lazy<TelemetryData> CreateLazy(byte[] raw) =>
        new(
            () => sessionTelemetryProcessor.ReadProcessedTelemetryData(raw),
            LazyThreadSafetyMode.ExecutionAndPublication);

    private void ClearRetainedValue(Guid sessionId)
    {
        lock (gate)
        {
            if (retained.TryGetValue(sessionId, out var entry))
            {
                entry.Hash = null;
                entry.Lazy = null;
            }
        }
    }

    private void ClearRetainedValue(Guid sessionId, string hash, Lazy<TelemetryData> lazy)
    {
        lock (gate)
        {
            if (retained.TryGetValue(sessionId, out var entry) &&
                StringComparer.Ordinal.Equals(entry.Hash, hash) &&
                ReferenceEquals(entry.Lazy, lazy))
            {
                entry.Hash = null;
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

    private static string ComputeHash(byte[] raw) =>
        Convert.ToHexStringLower(SHA256.HashData(raw));

    private sealed class RetainedTelemetry
    {
        public int RetentionCount { get; set; }

        public string? Hash { get; set; }

        public Lazy<TelemetryData>? Lazy { get; set; }
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

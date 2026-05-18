using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Services;

// Persistence boundary for SQLite-backed entities plus the session-specific
// blobs and sync state that do not fit the generic Synchronizable operations.
public interface IDatabaseService
{
    // Generic synchronizable-entity operations used by stores, coordinators,
    // and sync merge code.
    public Task<List<T>> GetAllAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : Synchronizable, new();
    public Task<List<T>> GetChangedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(long since) where T : Synchronizable, new();
    public Task<T> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Guid id) where T : Synchronizable, new();
    public Task<Guid> PutAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item) where T : Synchronizable, new();
    public Task DeleteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Guid id) where T : Synchronizable, new();
    public Task DeleteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item) where T : Synchronizable, new();
    // Session rows have auxiliary cache/source/track data, so their durable
    // writes stay grouped here instead of being spread across feature services.
    public Task<List<Session>> GetSessionsAsync();
    public Task<Session?> GetSessionAsync(Guid id);
    public Task<List<Guid>> GetIncompleteSessionIdsAsync();
    public Task<List<RecordedSessionSource>> GetRecordedSessionSourcesAsync();
    public Task<RecordedSessionSource?> GetRecordedSessionSourceAsync(Guid id);
    public Task<List<Guid>> GetSessionIdsMissingRecordedSourceAsync();
    public Task<TelemetryData?> GetSessionPsstAsync(Guid id);
    public Task<byte[]?> GetSessionRawPsstAsync(Guid id);
    public Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id);
    public Task<Guid> PutSessionAsync(Session session);
    public Task PutRecordedSessionSourceAsync(RecordedSessionSource source);
    public Task DeleteRecordedSessionSourceAsync(Guid sessionId);
    public Task<Session> PutProcessedSessionAsync(Session session, Track? newFullTrack, RecordedSessionSource? source);
    public Task<Session?> PutProcessedSessionIfUnchangedAsync(Session session, Track? newFullTrack, RecordedSessionSource? source, long baselineUpdated);
    public Task<Guid?> FindTrackByTimeRangeAsync(long startTime, long endTime);
    public Task PatchSessionPsstAsync(Guid id, byte[] data);
    public Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points);
    public Task<SessionCache?> GetSessionCacheAsync(Guid sessionId);
    public Task<Guid> PutSessionCacheAsync(SessionCache sessionCache);
    public Task<Guid?> AssociateSessionWithTrackAsync(Guid sessionId);
    // Cross-device sync uses these methods to read local deltas, apply remote
    // deltas, and persist per-server progress markers.
    public Task<long> GetLastSyncTimeAsync(string? serverUrl);
    public Task UpdateLastSyncTimeAsync(string? serverUrl);
    public Task<SynchronizationData> GetSynchronizationDataAsync(long since);
    public Task ApplyRemoteSynchronizationDataAsync(SynchronizationData data);
    // Pairing state is stored beside sync metadata because it determines which
    // remote devices are allowed to exchange those deltas.
    public Task<List<PairedDevice>> GetPairedDevicesAsync();
    public Task<PairedDevice?> GetPairedDeviceAsync(string id);
    public Task<PairedDevice?> GetPairedDeviceByTokenAsync(string token);
    public Task PutPairedDeviceAsync(PairedDevice device);
    public Task DeletePairedDeviceAsync(string id);
    public Task MergeAllAsync(SynchronizationData data);
}

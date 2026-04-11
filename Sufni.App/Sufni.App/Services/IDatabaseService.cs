using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Services;

public interface IDatabaseService
{
    public Task<List<T>> GetAllAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>() where T : Synchronizable, new();
    public Task<List<T>> GetChangedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(long since) where T : Synchronizable, new();
    public Task<T> GetAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Guid id) where T : Synchronizable, new();
    public Task<Guid> PutAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item) where T : Synchronizable, new();
    public Task DeleteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(Guid id) where T : Synchronizable, new();
    public Task DeleteAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(T item) where T : Synchronizable, new();
    public Task<List<Session>> GetSessionsAsync();
    public Task<Session?> GetSessionAsync(Guid id);
    public Task<List<Guid>> GetIncompleteSessionIdsAsync();
    public Task<TelemetryData?> GetSessionPsstAsync(Guid id);
    public Task<byte[]?> GetSessionRawPsstAsync(Guid id);
    public Task<List<TrackPoint>?> GetSessionTrackAsync(Guid id);
    public Task<Guid> PutSessionAsync(Session session);
    public Task DeleteSessionAsync(Guid id);
    public Task PatchSessionPsstAsync(Guid id, byte[] data);
    public Task PatchSessionTrackAsync(Guid id, List<TrackPoint> points);
    public Task<SessionCache?> GetSessionCacheAsync(Guid sessionId);
    public Task<Guid> PutSessionCacheAsync(SessionCache sessionCache);
    public Task<Guid?> AssociateSessionWithTrackAsync(Guid sessionId);
    public Task<long> GetLastSyncTimeAsync(string? serverUrl);
    public Task UpdateLastSyncTimeAsync(string? serverUrl);
    public Task<List<PairedDevice>> GetPairedDevicesAsync();
    public Task<PairedDevice?> GetPairedDeviceAsync(string id);
    public Task<PairedDevice?> GetPairedDeviceByTokenAsync(string token);
    public Task PutPairedDeviceAsync(PairedDevice device);
    public Task DeletePairedDeviceAsync(string id);
    public Task<string?> GetAppSettingAsync(string key);
    public Task PutAppSettingAsync(string key, string value);
    public Task MergeAllAsync(SynchronizationData data);
}
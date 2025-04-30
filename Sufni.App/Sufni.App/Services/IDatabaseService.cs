using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Services;

public interface IDatabaseService
{
    public Task<List<Board>> GetBoardsAsync();
    public Task<List<Board>> GetChangedBoardsAsync(int since);
    public Task PutBoardAsync(Board board);
    public Task DeleteBoardAsync(string id);
    public Task<List<Bike>> GetBikesAsync();
    public Task<List<Bike>> GetChangedBikesAsync(int since);
    public Task<Bike?> GetBikeAsync(Guid id);
    public Task<Guid> PutBikeAsync(Bike bike);
    public Task DeleteBikeAsync(Guid id);
    public Task<List<Setup>> GetSetupsAsync();
    public Task<List<Setup>> GetChangedSetupsAsync(int since);
    public Task<Setup?> GetSetupAsync(Guid id);
    public Task<Guid> PutSetupAsync(Setup setup);
    public Task DeleteSetupAsync(Guid id);
    public Task<List<Session>> GetSessionsAsync();
    public Task<List<Guid>> GetIncompleteSessionIdsAsync();
    public Task<List<Session>> GetChangedSessionsAsync(int since);
    public Task<TelemetryData?> GetSessionPsstAsync(Guid id);
    public Task<byte[]?> GetSessionRawPsstAsync(Guid id);
    public Task<Guid> PutSessionAsync(Session session);
    public Task PatchSessionPsstAsync(Guid id, byte[] data);
    public Task DeleteSessionAsync(Guid id);
    public Task<SessionCache?> GetSessionCacheAsync(Guid sessionId);
    public Task<Guid> PutSessionCacheAsync(SessionCache sessionCache);
    public Task<int> GetLastSyncTimeAsync();
    public Task UpdateLastSyncTimeAsync();
}
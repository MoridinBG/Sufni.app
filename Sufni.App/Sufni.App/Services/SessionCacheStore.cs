using System;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Services;

public interface ISessionCacheStore
{
    Task<SessionCache?> GetSessionCacheAsync(Guid sessionId);

    Task<Guid> PutSessionCacheAsync(SessionCache sessionCache);

    // Removes the mobile session_cache row for one session. Derived writers call
    // this after changing session.data or the cached session-window track so the
    // next mobile load rebuilds the cache from the fresh derived data.
    Task DeleteSessionCacheAsync(Guid sessionId);
}

internal sealed class SessionCacheStore(SqliteConnectionContext connectionContext) : ISessionCacheStore
{
    public async Task<SessionCache?> GetSessionCacheAsync(Guid sessionId)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<SessionCache>()
            .Where(cache => cache.SessionId == sessionId)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> PutSessionCacheAsync(SessionCache sessionCache)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var existing = await connection.Table<SessionCache>()
            .Where(cache => cache.SessionId == sessionCache.SessionId)
            .FirstOrDefaultAsync() is not null;
        if (existing)
        {
            await connection.UpdateAsync(sessionCache);
        }
        else
        {
            await connection.InsertAsync(sessionCache);
        }

        return sessionCache.SessionId;
    }

    public async Task DeleteSessionCacheAsync(Guid sessionId)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        await connection.ExecuteAsync(
            "DELETE FROM session_cache WHERE session_id = ?",
            sessionId);
    }
}

using System;
using System.Threading.Tasks;
using Sufni.App.Models;

namespace Sufni.App.Services;

public interface ISessionCacheStore
{
    Task<SessionCache?> GetSessionCacheAsync(Guid sessionId);

    Task<Guid> PutSessionCacheAsync(SessionCache sessionCache);
}

internal sealed class SessionCacheStore(SqLiteDatabaseService databaseService) : ISessionCacheStore
{
    public async Task<SessionCache?> GetSessionCacheAsync(Guid sessionId)
    {
        var connection = await databaseService.GetInitializedConnectionAsync();
        return await connection.Table<SessionCache>()
            .Where(cache => cache.SessionId == sessionId)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> PutSessionCacheAsync(SessionCache sessionCache)
    {
        var connection = await databaseService.GetInitializedConnectionAsync();
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
}

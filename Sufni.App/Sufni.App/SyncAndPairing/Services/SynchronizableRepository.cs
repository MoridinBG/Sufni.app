using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;

using static Sufni.App.Infrastructure.PersistenceGuards;

using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.SyncAndPairing.Services;

public interface ISynchronizableRepository<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    where T : Synchronizable, new()
{
    Task<List<T>> GetAllAsync();

    Task<List<T>> GetChangedAsync(long sinceExclusive, long upperInclusive);

    Task<T?> GetAsync(Guid id);

    Task<Guid> PutAsync(T item);

    Task DeleteAsync(Guid id);

    Task DeleteAsync(T item);
}

internal sealed class SynchronizableRepository<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
    SqliteConnectionContext connectionContext,
    TimeProvider timeProvider)
    : ISynchronizableRepository<T>
    where T : Synchronizable, new()
{
    public async Task<List<T>> GetAllAsync()
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<T>()
            .Where(entity => entity.Deleted == null)
            .ToListAsync();
    }

    public async Task<List<T>> GetChangedAsync(long sinceExclusive, long upperInclusive)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<T>()
            .Where(entity =>
                (entity.Updated > sinceExclusive && entity.Updated <= upperInclusive) ||
                (entity.Deleted != null &&
                    entity.Deleted > sinceExclusive &&
                    entity.Deleted <= upperInclusive))
            .ToListAsync();
    }

    public async Task<T?> GetAsync(Guid id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<T>()
            .Where(entity => entity.Id == id && entity.Deleted == null)
            .FirstOrDefaultAsync();
    }

    public async Task<Guid> PutAsync(T item)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await PutCoreAsync(connection, item);
    }

    public Task DeleteAsync(Guid id) =>
        connectionContext.RunInTransactionAsync(connection => DeleteInTransaction(connection, id, timeProvider.GetUtcNow().ToUnixTimeSeconds()));

    public Task DeleteAsync(T item) => DeleteAsync(item.Id);

    internal static Guid PutInTransaction(SQLiteConnection connection, T item, long now)
    {
        var existing = EntityExists<T>(connection, item.Id);
        item.Updated = now;
        item.Deleted = null;
        if (existing)
        {
            UpdateEntity(connection, item);
        }
        else
        {
            InsertEntity(connection, item);
        }

        return item.Id;
    }

    internal static void DeleteInTransaction(SQLiteConnection connection, Guid id, long now)
    {
        var itemFromDatabase = connection.Find<T>(id);
        if (itemFromDatabase is not null && itemFromDatabase.Deleted is null)
        {
            itemFromDatabase.Deleted = now;
            UpdateEntity(connection, itemFromDatabase);
        }
    }

    private async Task<Guid> PutCoreAsync(SQLiteAsyncConnection connection, T item)
    {
        var existing = await EntityExistsAsync<T>(connection, item.Id);
        item.Updated = timeProvider.GetUtcNow().ToUnixTimeSeconds();
        item.Deleted = null;
        if (existing)
        {
            await UpdateEntityAsync(connection, item);
        }
        else
        {
            await InsertEntityAsync(connection, item);
        }

        return item.Id;
    }
}

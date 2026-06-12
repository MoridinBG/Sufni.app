using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.Models;

using static Sufni.App.Services.PersistenceGuards;

namespace Sufni.App.Services;

public interface ISynchronizableRepository<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>
    where T : Synchronizable, new()
{
    Task<List<T>> GetAllAsync();

    Task<List<T>> GetChangedAsync(long since);

    Task<T?> GetAsync(Guid id);

    Task<Guid> PutAsync(T item);

    Task DeleteAsync(Guid id);

    Task DeleteAsync(T item);
}

internal sealed class SynchronizableRepository<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(
    SqliteConnectionContext connectionContext)
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

    public async Task<List<T>> GetChangedAsync(long since)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        return await connection.Table<T>()
            .Where(entity => entity.Updated > since || (entity.Deleted != null && entity.Deleted > since))
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
        var existing = await EntityExistsAsync<T>(connection, item.Id);
        item.Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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

    public async Task DeleteAsync(Guid id)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var item = await connection.Table<T>()
            .Where(entity => entity.Id == id)
            .FirstOrDefaultAsync();
        if (item is not null && item.Deleted is null)
        {
            item.Deleted = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await UpdateEntityAsync(connection, item);
        }
    }

    public async Task DeleteAsync(T item)
    {
        var connection = await connectionContext.GetInitializedConnectionAsync();
        var itemFromDatabase = await connection.Table<T>()
            .Where(entity => entity.Id == item.Id)
            .FirstOrDefaultAsync();
        if (itemFromDatabase is not null && itemFromDatabase.Deleted is null)
        {
            itemFromDatabase.Deleted = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await UpdateEntityAsync(connection, itemFromDatabase);
        }
    }

}

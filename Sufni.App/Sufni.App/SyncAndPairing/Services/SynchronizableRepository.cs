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
    TimeProvider timeProvider,
    ILocalDeviceIdentity localDeviceIdentity)
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
            .Where(entity => entity.LocalSequence > sinceExclusive && entity.LocalSequence <= upperInclusive)
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
        var writer = await localDeviceIdentity.GetIdAsync();
        return await connectionContext.RunInTransactionAsync(connection =>
            PutInTransaction(connection, item, timeProvider.GetUtcNow().ToUnixTimeSeconds(), writer));
    }

    public async Task DeleteAsync(Guid id)
    {
        var writer = await localDeviceIdentity.GetIdAsync();
        await connectionContext.RunInTransactionAsync(connection =>
            DeleteInTransaction(connection, id, timeProvider.GetUtcNow().ToUnixTimeSeconds(), writer));
    }

    public Task DeleteAsync(T item) => DeleteAsync(item.Id);

    internal static Guid PutInTransaction(SQLiteConnection connection, T item, long now, string writer)
    {
        var existing = EntityExists<T>(connection, item.Id);
        SynchronizationWriteMetadata.PrepareAuthoredWrite(connection, item, now, writer);
        item.Deleted = null;
        if (existing) UpdateEntity(connection, item);
        else InsertEntity(connection, item);
        return item.Id;
    }

    internal static void DeleteInTransaction(SQLiteConnection connection, Guid id, long now, string writer)
    {
        var stored = connection.Find<T>(id);
        if (stored is null || stored.Deleted is not null) return;
        SynchronizationWriteMetadata.PrepareAuthoredWrite(connection, stored, now, writer);
        stored.Deleted = now;
        UpdateEntity(connection, stored);
        SynchronizationWriteMetadata.RecordDeletion(connection, stored);
    }
}

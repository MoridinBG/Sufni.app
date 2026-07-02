using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;

using static Sufni.App.Infrastructure.PersistenceGuards;

using Sufni.App.Bikes.Models;
using Sufni.App.Extensibility.Database;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Setups.Models;
using Sufni.App.SyncAndPairing.Models;
namespace Sufni.App.SyncAndPairing.Services;

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
    SqliteConnectionContext connectionContext,
    IExtensionCascadeService? extensionCascadeService = null)
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
        await DeleteCoreAsync(id);
    }

    public async Task DeleteAsync(T item)
    {
        await DeleteCoreAsync(item.Id);
    }

    private async Task DeleteCoreAsync(Guid id)
    {
        var rulesApplied = false;
        await connectionContext.RunInTransactionAsync(connection =>
        {
            var itemFromDatabase = connection.Find<T>(id);
            if (itemFromDatabase is not null && itemFromDatabase.Deleted is null)
            {
                itemFromDatabase.Deleted = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                UpdateEntity(connection, itemFromDatabase);
            }

            if (extensionCascadeService is not null)
            {
                rulesApplied = extensionCascadeService.ApplyRulesForDeletedCoreEntityInTransaction(
                    connection,
                    GetCoreEntityKind(),
                    id);
            }
        });

        if (rulesApplied && extensionCascadeService is not null)
        {
            await extensionCascadeService.RefreshExtensionStateAsync();
        }
    }

    private static ExtensionCoreEntityKind GetCoreEntityKind()
    {
        var entityType = typeof(T);
        if (entityType == typeof(Board))
        {
            return ExtensionCoreEntityKind.Board;
        }

        if (entityType == typeof(Bike))
        {
            return ExtensionCoreEntityKind.Bike;
        }

        if (entityType == typeof(Setup))
        {
            return ExtensionCoreEntityKind.Setup;
        }

        if (entityType == typeof(Session))
        {
            return ExtensionCoreEntityKind.Session;
        }

        if (entityType == typeof(Track))
        {
            return ExtensionCoreEntityKind.Track;
        }

        throw new InvalidOperationException($"Unsupported synchronizable entity type '{entityType.FullName}'.");
    }
}

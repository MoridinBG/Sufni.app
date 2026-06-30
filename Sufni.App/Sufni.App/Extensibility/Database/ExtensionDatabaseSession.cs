using System;
using System.Threading.Tasks;
using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;

namespace Sufni.App.Extensibility.Database;

internal sealed class ExtensionDatabaseSession(
    SQLiteAsyncConnection connection,
    ExtensionDatabaseTableCatalog tableCatalog) : IExtensionDatabaseSession
{
    public AsyncTableQuery<T> Table<T>()
        where T : new()
    {
        ValidateTable<T>();
        return connection.Table<T>();
    }

    public async Task<T?> FindAsync<T>(object primaryKey)
        where T : new()
    {
        ValidateTable<T>();
        return await connection.FindAsync<T>(primaryKey);
    }

    public Task<int> InsertAsync<T>(T row)
    {
        ValidateTable<T>();
        return connection.InsertAsync(row);
    }

    public Task<int> InsertOrReplaceAsync<T>(T row)
    {
        ValidateTable<T>();
        return connection.InsertOrReplaceAsync(row);
    }

    public Task<int> UpdateAsync<T>(T row)
    {
        ValidateTable<T>();
        return connection.UpdateAsync(row);
    }

    public async Task<int> DeleteAsync<T>(object primaryKey)
        where T : new()
    {
        ValidateTable<T>();
        var row = await connection.FindAsync<T>(primaryKey);
        return row is null ? 0 : await connection.DeleteAsync(row);
    }

    private void ValidateTable<T>()
    {
        var tableType = typeof(T);
        if (!tableCatalog.IsDeclaredTableType(tableType))
        {
            throw new InvalidOperationException(
                $"Extension database table type '{tableType.FullName}' is not declared by a registered extension migrator.");
        }
    }
}

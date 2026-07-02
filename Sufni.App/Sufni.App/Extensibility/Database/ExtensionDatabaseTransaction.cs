using System;
using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;

namespace Sufni.App.Extensibility.Database;

internal sealed class ExtensionDatabaseTransaction(
    SQLiteConnection connection,
    ExtensionDatabaseTableCatalog tableCatalog) : IExtensionDatabaseTransaction
{
    public TableQuery<T> Table<T>()
        where T : new()
    {
        ValidateTable<T>();
        return connection.Table<T>();
    }

    public T? Find<T>(object primaryKey)
        where T : new()
    {
        ValidateTable<T>();
        return connection.Find<T>(primaryKey);
    }

    public int Insert<T>(T row)
    {
        ValidateTable<T>();
        return connection.Insert(row);
    }

    public int InsertOrReplace<T>(T row)
    {
        ValidateTable<T>();
        return connection.InsertOrReplace(row);
    }

    public int Update<T>(T row)
    {
        ValidateTable<T>();
        return connection.Update(row);
    }

    public int Delete<T>(object primaryKey)
        where T : new()
    {
        ValidateTable<T>();
        var row = connection.Find<T>(primaryKey);
        return row is null ? 0 : connection.Delete(row);
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

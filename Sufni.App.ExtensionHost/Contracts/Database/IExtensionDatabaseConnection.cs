using System;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace Sufni.App.ExtensionHost.Contracts.Database;

public interface IExtensionDatabaseConnection
{
    Task<IExtensionDatabaseSession> OpenSessionAsync(CancellationToken cancellationToken = default);
}

public interface IExtensionDatabaseSession
{
    AsyncTableQuery<T> Table<T>()
        where T : new();

    Task<T?> FindAsync<T>(object primaryKey)
        where T : new();

    Task<int> InsertAsync<T>(T row);

    Task<int> InsertOrReplaceAsync<T>(T row);

    Task<int> UpdateAsync<T>(T row);

    Task<int> DeleteAsync<T>(object primaryKey)
        where T : new();

    Task RunInTransactionAsync(Action<IExtensionDatabaseTransaction> work);
}

public interface IExtensionDatabaseTransaction
{
    TableQuery<T> Table<T>()
        where T : new();

    T? Find<T>(object primaryKey)
        where T : new();

    int Insert<T>(T row);

    int InsertOrReplace<T>(T row);

    int Update<T>(T row);

    int Delete<T>(object primaryKey)
        where T : new();
}

using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace Sufni.App.ExtensionHost.Database;

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
}

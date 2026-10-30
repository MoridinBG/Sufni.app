using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace Sufni.App.Infrastructure;

internal sealed class SqliteConnectionContext
{
    internal SqliteConnectionContext(string databasePath, TimeProvider timeProvider)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Connection = new SQLiteAsyncConnection(databasePath);
        Initialization = new DatabaseMigrationRunner(databasePath, Connection, timeProvider).RunAsync();
    }

    internal SQLiteAsyncConnection Connection { get; }

    internal Task Initialization { get; }

    internal async Task<SQLiteAsyncConnection> GetInitializedConnectionAsync(CancellationToken cancellationToken = default)
    {
        await Initialization.WaitAsync(cancellationToken);
        return Connection;
    }

    internal async Task<T> RunInTransactionAsync<T>(
        Func<SQLiteConnection, T> work,
        CancellationToken cancellationToken = default)
    {
        await Initialization.WaitAsync(cancellationToken);
        T result = default!;
        await Connection.RunInTransactionAsync(connection =>
        {
            result = work(connection);
        });
        return result;
    }

    internal async Task RunInTransactionAsync(
        Action<SQLiteConnection> work,
        CancellationToken cancellationToken = default)
    {
        await Initialization.WaitAsync(cancellationToken);
        await Connection.RunInTransactionAsync(work);
    }
}

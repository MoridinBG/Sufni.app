using System.Collections.Generic;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace Sufni.App.Infrastructure;

internal sealed class SqliteConnectionContext : IStartupDatabase, IAsyncDisposable
{
    private readonly string databasePath;
    private readonly TimeProvider timeProvider;
    private readonly IReadOnlyDictionary<Guid, int> legacyProcessingWindows;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;

    internal SqliteConnectionContext(
        string databasePath, TimeProvider timeProvider, IBackgroundTaskRunner backgroundTaskRunner,
        IReadOnlyDictionary<Guid, int> legacyProcessingWindows)
    {
        this.databasePath = databasePath;
        this.legacyProcessingWindows = legacyProcessingWindows;
        this.timeProvider = timeProvider;
        this.backgroundTaskRunner = backgroundTaskRunner;
        Connection = new SQLiteAsyncConnection(databasePath);
        Initialization = backgroundTaskRunner.RunAsync(async () =>
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await new DatabaseMigrationRunner(databasePath, Connection, timeProvider, legacyProcessingWindows).RunAsync();
        });
    }

    internal SQLiteAsyncConnection Connection { get; }

    public Task Initialization { get; }

    public Task<string> ArchiveAndResetAsync() => backgroundTaskRunner.RunAsync(async () =>
    {
        await DisposeAsync();
        var archiveDirectory = $"{databasePath}.archive-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss-fffffff}";
        Directory.CreateDirectory(archiveDirectory);
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Move(path, Path.Combine(archiveDirectory, Path.GetFileName(path)));
            }
        }

        var freshConnection = new SQLiteAsyncConnection(databasePath);
        try
        {
            await new DatabaseMigrationRunner(databasePath, freshConnection, timeProvider, legacyProcessingWindows).RunAsync();
        }
        finally
        {
            await freshConnection.CloseAsync();
        }

        return archiveDirectory;
    });

    public async ValueTask DisposeAsync()
    {
        // Failed initialization must still release its connection for reset or shutdown.
        await Initialization.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await Connection.CloseAsync();
    }

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

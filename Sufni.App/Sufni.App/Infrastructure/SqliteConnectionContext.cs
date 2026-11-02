using Sufni.App.MapsAndTracks.Stores;
using System.Collections.Generic;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace Sufni.App.Infrastructure;

internal sealed class SqliteConnectionContext : IStartupDatabase, IAsyncDisposable
{
    private readonly SQLiteConnectionString connectionString;
    private string DatabasePath => connectionString.DatabasePath;
    private readonly TimeProvider timeProvider;
    private readonly IReadOnlyDictionary<Guid, int> legacyProcessingWindows;
    private readonly IReadOnlyList<CustomTileLayerSnapshot> legacyCustomLayers;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;

    internal SqliteConnectionContext(
        SQLiteConnectionString connectionString, TimeProvider timeProvider, IBackgroundTaskRunner backgroundTaskRunner,
        IReadOnlyDictionary<Guid, int> legacyProcessingWindows, IReadOnlyList<CustomTileLayerSnapshot> legacyCustomLayers)
    {
        this.connectionString = connectionString;
        this.legacyProcessingWindows = legacyProcessingWindows;
        this.legacyCustomLayers = legacyCustomLayers;
        this.timeProvider = timeProvider;
        this.backgroundTaskRunner = backgroundTaskRunner;
        Connection = new SQLiteAsyncConnection(connectionString);
        Initialization = backgroundTaskRunner.RunAsync(async () =>
        {
            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await new DatabaseMigrationRunner(DatabasePath, Connection, timeProvider, legacyProcessingWindows, legacyCustomLayers).RunAsync();
        });
    }

    internal SQLiteAsyncConnection Connection { get; }

    public Task Initialization { get; }

    public Task<string> ArchiveAndResetAsync() => backgroundTaskRunner.RunAsync(async () =>
    {
        await DisposeAsync();
        var archiveDirectory = $"{DatabasePath}.archive-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss-fffffff}";
        Directory.CreateDirectory(archiveDirectory);
        foreach (var path in new[] { DatabasePath, DatabasePath + "-wal", DatabasePath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Move(path, Path.Combine(archiveDirectory, Path.GetFileName(path)));
            }
        }

        var freshConnection = new SQLiteAsyncConnection(connectionString);
        try
        {
            await new DatabaseMigrationRunner(DatabasePath, freshConnection, timeProvider, legacyProcessingWindows, legacyCustomLayers).RunAsync();
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

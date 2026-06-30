using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;

using Sufni.App.Extensibility.Database;
namespace Sufni.App.ExtensionHost.TestSupport;

/// <summary>
/// Exercises the real extension-host database machinery
/// (<c>ExtensionDatabaseMigratorRunner</c>, <c>ExtensionDatabaseSession</c>,
/// <c>ExtensionCascadeService</c>) over a temporary database, so host
/// behavior changes fail extension tests instead of drifting silently.
/// </summary>
public sealed class TestExtensionHostHarness : IAsyncDisposable
{
    private readonly TempDatabase tempDatabase;
    private readonly IReadOnlyList<IExtensionDatabaseMigrator> migrators;
    private readonly ExtensionDatabaseTableCatalog tableCatalog;

    public SQLiteAsyncConnection Connection { get; }

    private TestExtensionHostHarness(
        TempDatabase tempDatabase,
        SQLiteAsyncConnection connection,
        IReadOnlyList<IExtensionDatabaseMigrator> migrators)
    {
        this.tempDatabase = tempDatabase;
        Connection = connection;
        this.migrators = migrators;
        tableCatalog = ExtensionDatabaseTableCatalog.Create(migrators);
    }

    public static async Task<TestExtensionHostHarness> CreateAsync(
        params IExtensionDatabaseMigrator[] migrators)
    {
        var tempDatabase = new TempDatabase("extension-host.db", "sufni-extension-host-test");
        var connection = new SQLiteAsyncConnection(tempDatabase.DatabasePath);
        try
        {
            await new ExtensionDatabaseMigratorRunner(migrators).RunAsync(connection);
        }
        catch
        {
            await connection.CloseAsync();
            tempDatabase.Dispose();
            throw;
        }

        return new TestExtensionHostHarness(tempDatabase, connection, migrators);
    }

    public IExtensionDatabaseConnection CreateDatabaseConnection() =>
        new HarnessDatabaseConnection(Connection, tableCatalog);

    public IExtensionCascadeService CreateCascadeService(
        IEnumerable<IExtensionCascadeRuleProvider> ruleProviders,
        IEnumerable<IExtensionStateRefreshParticipant> refreshParticipants) =>
        new ExtensionCascadeService(Connection, migrators, ruleProviders, refreshParticipants);

    public async Task<int?> GetSchemaVersionAsync(string extensionId)
    {
        var version = await Connection.FindAsync<ExtensionSchemaVersion>(extensionId);
        return version?.Version;
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.CloseAsync();
        tempDatabase.Dispose();
    }

    private sealed class HarnessDatabaseConnection(
        SQLiteAsyncConnection connection,
        ExtensionDatabaseTableCatalog tableCatalog) : IExtensionDatabaseConnection
    {
        public Task<IExtensionDatabaseSession> OpenSessionAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IExtensionDatabaseSession>(
                new ExtensionDatabaseSession(connection, tableCatalog));
        }
    }
}

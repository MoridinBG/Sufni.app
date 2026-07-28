using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.Extensibility.Database;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Extensibility.Database;

public class ExtensionDatabaseConnectionTests
{
    [Fact]
    public async Task OpenSessionAsync_WaitsForExtensionMigrations()
    {
        using var tempDatabase = new TempDatabase("extension-raw-connection.db");
        var databasePath = tempDatabase.DatabasePath;
        var migrator = new TestExtensionMigrator(
            "test",
            targetVersion: 1,
            [typeof(TestExtensionRow)],
            [
                new ExtensionDatabaseMigrationStep(1, context =>
                {
                    context.Transaction.Insert(new TestExtensionRow
                    {
                        Id = "ready",
                        Value = 1,
                    });
                }),
            ]);

        IExtensionDatabaseConnection database = new ExtensionDatabaseConnection(
            PersistenceTestData.CreateConnectionContext(databasePath, [migrator]));

        var session = await database.OpenSessionAsync();
        var rows = await session.Table<TestExtensionRow>().ToListAsync();

        Assert.Single(rows);
        Assert.Equal("ready", rows[0].Id);

    }

    [Fact]
    public async Task OpenSessionAsync_RejectsUndeclaredAndCoreTableTypes()
    {
        using var tempDatabase = new TempDatabase("extension-owned-session.db");
        var databasePath = tempDatabase.DatabasePath;

        IExtensionDatabaseConnection database = new ExtensionDatabaseConnection(
            PersistenceTestData.CreateConnectionContext(
                databasePath,
                [new TestExtensionMigrator("test", targetVersion: 0, [typeof(TestExtensionRow)], [])]));
        var session = await database.OpenSessionAsync();

        _ = session.Table<TestExtensionRow>();
        var undeclaredException = Assert.Throws<InvalidOperationException>(
            () => session.Table<SecondTestExtensionRow>());
        var coreException = await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.InsertAsync(new CoreNamedExtensionRow { Id = "core" }));

        Assert.Contains(typeof(SecondTestExtensionRow).FullName!, undeclaredException.Message);
        Assert.Contains(typeof(CoreNamedExtensionRow).FullName!, coreException.Message);
    }

    [Fact]
    public async Task RunInTransactionAsync_RollsBack_WhenCallbackThrows()
    {
        using var tempDatabase = new TempDatabase("extension-transaction-rollback.db");
        var databasePath = tempDatabase.DatabasePath;

        IExtensionDatabaseConnection database = new ExtensionDatabaseConnection(
            PersistenceTestData.CreateConnectionContext(
                databasePath,
                [new TestExtensionMigrator("test", targetVersion: 0, [typeof(TestExtensionRow)], [])]));
        var session = await database.OpenSessionAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.RunInTransactionAsync(transaction =>
            {
                transaction.Insert(new TestExtensionRow { Id = "rolled-back", Value = 10 });
                throw new InvalidOperationException("fail transaction");
            }));

        Assert.Null(await session.FindAsync<TestExtensionRow>("rolled-back"));
    }

    [Fact]
    public async Task RunInTransactionAsync_RejectsUndeclaredAndCoreTableTypes()
    {
        using var tempDatabase = new TempDatabase("extension-transaction-validation.db");
        var databasePath = tempDatabase.DatabasePath;

        IExtensionDatabaseConnection database = new ExtensionDatabaseConnection(
            PersistenceTestData.CreateConnectionContext(
                databasePath,
                [new TestExtensionMigrator("test", targetVersion: 0, [typeof(TestExtensionRow)], [])]));
        var session = await database.OpenSessionAsync();

        await session.RunInTransactionAsync(transaction =>
        {
            _ = transaction.Table<TestExtensionRow>();
        });
        var undeclaredException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.RunInTransactionAsync(transaction =>
            {
                _ = transaction.Table<SecondTestExtensionRow>();
            }));
        var coreException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            session.RunInTransactionAsync(transaction =>
            {
                transaction.Insert(new CoreNamedExtensionRow { Id = "core" });
            }));

        Assert.Contains(typeof(SecondTestExtensionRow).FullName!, undeclaredException.Message);
        Assert.Contains(typeof(CoreNamedExtensionRow).FullName!, coreException.Message);
    }
}

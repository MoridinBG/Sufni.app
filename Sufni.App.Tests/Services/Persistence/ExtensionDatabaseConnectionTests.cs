using SQLite;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Tests.TestSupport;
using Sufni.Telemetry;

using Sufni.App.Extensibility.Database;
namespace Sufni.App.Tests.Services.Persistence;

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
                new ExtensionDatabaseMigrationStep(1, async (context, _) =>
                {
                    await context.Database.InsertAsync(new TestExtensionRow
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
}

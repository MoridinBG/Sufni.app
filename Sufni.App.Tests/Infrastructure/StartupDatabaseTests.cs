using SQLite;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Persistence;

namespace Sufni.App.Tests.Infrastructure;

public class StartupDatabaseTests
{
    [Fact]
    public async Task Reset_ArchivesExistingData_AndCreatesANewEmptyDatabaseIdentity()
    {
        using var temp = new TempDatabase();
        await using var context = PersistenceTestData.CreateConnectionContext(temp.DatabasePath);
        IStartupDatabase database = context;
        await database.Initialization;
        var bike = new Bike(Guid.NewGuid(), "archived bike");
        await context.Connection.InsertAsync(bike);
        var originalIdentity = await context.Connection.ExecuteScalarAsync<string>(
            "SELECT db_incarnation FROM database_identity");

        var archive = await database.ArchiveAndResetAsync();

        using var archived = new SQLiteConnection(Path.Combine(archive, Path.GetFileName(temp.DatabasePath)));
        Assert.Equal(bike.Name, archived.Find<Bike>(bike.Id).Name);
        Assert.Equal(originalIdentity, archived.ExecuteScalar<string>("SELECT db_incarnation FROM database_identity"));
        using var fresh = new SQLiteConnection(temp.DatabasePath);
        Assert.Empty(fresh.Table<Bike>());
        Assert.NotEqual(originalIdentity, fresh.ExecuteScalar<string>("SELECT db_incarnation FROM database_identity"));
        Assert.Equal(1, fresh.ExecuteScalar<int>("SELECT COUNT(*) FROM core_migration WHERE id = 'database-incarnation'"));
    }

    [Fact]
    public async Task Reset_AfterInitializationFailure_PreservesCorruptFileAndCreatesUsableDatabase()
    {
        using var temp = new TempDatabase();
        byte[] corruptContents = "not a SQLite database"u8.ToArray();
        await File.WriteAllBytesAsync(temp.DatabasePath, corruptContents, TestContext.Current.CancellationToken);
        await using var context = PersistenceTestData.CreateConnectionContext(temp.DatabasePath);
        IStartupDatabase database = context;
        await Assert.ThrowsAnyAsync<Exception>(() => database.Initialization);

        var archive = await database.ArchiveAndResetAsync();

        Assert.Equal(corruptContents, await File.ReadAllBytesAsync(
            Path.Combine(archive, Path.GetFileName(temp.DatabasePath)), TestContext.Current.CancellationToken));
        using var fresh = new SQLiteConnection(temp.DatabasePath);
        Assert.Empty(fresh.Table<Bike>());
        Assert.NotEqual(Guid.Empty, Guid.Parse(fresh.ExecuteScalar<string>("SELECT db_incarnation FROM database_identity")));
        Assert.True(database.Initialization.IsFaulted);
    }
}

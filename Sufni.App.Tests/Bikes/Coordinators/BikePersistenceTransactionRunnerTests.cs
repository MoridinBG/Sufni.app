using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
using Sufni.App.Tests.TestSupport.Persistence;
namespace Sufni.App.Tests.Bikes.Coordinators;

public class BikePersistenceTransactionRunnerTests
{
    [Fact]
    public async Task DeleteBikeAsync_ReturnsInUse_AndKeepsBike_WhenActiveSetupReferencesIt()
    {
        await using var context = PersistenceTestData.CreateConnectionContext($"bike-in-use-{Guid.NewGuid():N}", inMemory: true);
        var connection = await context.GetInitializedConnectionAsync(TestContext.Current.CancellationToken);
        var runner = new BikePersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());
        var bike = new Bike(Guid.NewGuid(), "bike") { Version = 3 };
        await connection.InsertAsync(bike);
        await connection.InsertAsync(new Setup(Guid.NewGuid(), "setup") { BikeId = bike.Id });

        var result = await runner.DeleteBikeAsync(bike.Id, TestContext.Current.CancellationToken);

        Assert.IsType<BikeDeletePersistenceResult.InUse>(result);
        var retained = await connection.GetAsync<Bike>(bike.Id);
        Assert.Null(retained.Deleted);
        Assert.Equal(3, retained.Version);
    }

    [Fact]
    public async Task DeleteBikeAsync_DeletesBike_WhenOnlyDeletedSetupsReferenceIt()
    {
        await using var context = PersistenceTestData.CreateConnectionContext($"bike-delete-{Guid.NewGuid():N}", inMemory: true);
        var connection = await context.GetInitializedConnectionAsync(TestContext.Current.CancellationToken);
        var runner = new BikePersistenceTransactionRunner(context, PersistenceTestData.CreateTimeProvider(), PersistenceTestData.CreateDeviceIdentity());
        var bike = new Bike(Guid.NewGuid(), "bike");
        await connection.InsertAsync(bike);
        await connection.InsertAsync(new Setup(Guid.NewGuid(), "deleted setup") { BikeId = bike.Id, Deleted = 1 });

        var result = await runner.DeleteBikeAsync(bike.Id, TestContext.Current.CancellationToken);

        Assert.IsType<BikeDeletePersistenceResult.Deleted>(result);
        Assert.NotNull((await connection.GetAsync<Bike>(bike.Id)).Deleted);
    }
}

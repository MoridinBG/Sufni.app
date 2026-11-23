using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Bikes.Stores;
using Sufni.Telemetry;
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

    [Fact]
    public async Task SaveAsync_IgnoresTimestampAndCutoffChanges_AndPreservesCurrentCutoffs()
    {
        await using var database = new TestPersistenceHarness($"bike-{Guid.NewGuid():N}", inMemory: true);
        var baseline = TestSnapshots.Bike(updated: 5);
        var cutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        var current = baseline with { Updated = 20, DampingSpeedCutoffs = cutoffs };
        await (await database.GetInitializedConnectionAsync()).InsertAsync(Bike.FromSnapshot(current, []));
        var draft = BikeDraft.From(baseline) with { Name = "edited" };

        var result = Assert.IsType<BikeSaveResult.Saved>(await database.BikeTransactions.SaveAsync(draft, BikeDraft.From(baseline)));

        Assert.Equal("edited", result.Snapshot.Name);
        Assert.Equal(cutoffs, result.Snapshot.DampingSpeedCutoffs);
        Assert.Equal(cutoffs, (await database.GetRepository<Bike>().GetAsync(baseline.Id))!.DampingSpeedCutoffs);
    }

    [Fact]
    public async Task SaveAsync_NewDraftPreservesImportedCutoffs()
    {
        await using var database = new TestPersistenceHarness($"bike-{Guid.NewGuid():N}", inMemory: true);
        var draft = BikeDraft.From(TestSnapshots.Bike()) with { InitialDampingSpeedCutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400) };

        var saved = Assert.IsType<BikeSaveResult.Saved>(await database.BikeTransactions.SaveAsync(draft, null));

        Assert.Equal(draft.InitialDampingSpeedCutoffs, saved.Snapshot.DampingSpeedCutoffs);
        Assert.Equal(draft.InitialDampingSpeedCutoffs, (await database.GetRepository<Bike>().GetAsync(draft.Id))!.DampingSpeedCutoffs);
    }

    [Fact]
    public async Task SaveAsync_DoesNotRecreateRemovedExistingBike()
    {
        await using var database = new TestPersistenceHarness($"bike-{Guid.NewGuid():N}", inMemory: true);
        var original = TestSnapshots.Bike();
        await (await database.GetInitializedConnectionAsync()).InsertAsync(Bike.FromSnapshot(original, []));
        await database.GetRepository<Bike>().DeleteAsync(original.Id);

        var result = await database.BikeTransactions.SaveAsync(BikeDraft.From(original) with { Name = "later" }, BikeDraft.From(original));

        Assert.IsType<BikeSaveResult.Missing>(result);
        Assert.Null(await database.GetRepository<Bike>().GetAsync(original.Id));
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ReturnsMissing_WhenBikeIsGone()
    {
        await using var database = new TestPersistenceHarness($"bike-{Guid.NewGuid():N}", inMemory: true);
        var result = await database.BikeTransactions.UpdateCutoffAsync(
            Guid.NewGuid(), 5, SuspensionType.Front, DampingSpeedCircuit.Compression, 300);

        Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Missing>(result);
        Assert.Empty(await database.GetRepository<Bike>().GetAllAsync());
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_PreservesUnrelatedMetadataAndCutoffChanges()
    {
        await using var database = new TestPersistenceHarness($"bike-{Guid.NewGuid():N}", inMemory: true);
        var current = TestSnapshots.Bike(name: "Renamed elsewhere", updated: 100) with
        {
            RearCompressionDampingCutoffMmPerSecond = 450,
        };
        await (await database.GetInitializedConnectionAsync()).InsertAsync(Bike.FromSnapshot(current, []));

        var result = await database.BikeTransactions.UpdateCutoffAsync(
            current.Id, 200, SuspensionType.Front, DampingSpeedCircuit.Rebound, 310);

        var saved = Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Saved>(result);
        Assert.Equal(current.Name, saved.Snapshot.Name);
        Assert.Equal(450, saved.Snapshot.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(310, saved.Snapshot.FrontReboundDampingCutoffMmPerSecond);
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ReturnsConflict_WhenTargetCutoffChangedAtSameTimestamp()
    {
        await using var database = new TestPersistenceHarness($"bike-{Guid.NewGuid():N}", inMemory: true);
        var current = TestSnapshots.Bike(updated: 5) with { FrontReboundDampingCutoffMmPerSecond = 240 };
        await (await database.GetInitializedConnectionAsync()).InsertAsync(Bike.FromSnapshot(current, []));

        var result = await database.BikeTransactions.UpdateCutoffAsync(
            current.Id,
            expected: 200,
            SuspensionType.Front,
            DampingSpeedCircuit.Rebound,
            300);

        var conflict = Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Conflict>(result);
        Assert.Equal(current, conflict.CurrentSnapshot);
        Assert.Equal(BikeSnapshot.From((await database.GetRepository<Bike>().GetAsync(current.Id))!), current);
    }

    [Fact]
    public async Task SaveAsync_ReturnsConflict_WhenAuthoredFieldsChangedAtSameTimestamp_AndDoesNotPersist()
    {
        await using var database = new TestPersistenceHarness($"bike-{Guid.NewGuid():N}", inMemory: true);
        var current = TestSnapshots.Bike(updated: 10);
        await (await database.GetInitializedConnectionAsync()).InsertAsync(Bike.FromSnapshot(current, []));

        var bike = new Bike(current.Id, "stale edit") { HeadAngle = 65, ForkStroke = 160 };

        var result = await database.BikeTransactions.SaveAsync(BikeDraft.From(bike), BikeDraft.From(current with { Name = "original name" }));

        var conflict = Assert.IsType<BikeSaveResult.Conflict>(result);
        Assert.Equal(BikeDraft.From(current), conflict.CurrentDraft);
        Assert.Equal(BikeSnapshot.From((await database.GetRepository<Bike>().GetAsync(current.Id))!), current);
    }
}

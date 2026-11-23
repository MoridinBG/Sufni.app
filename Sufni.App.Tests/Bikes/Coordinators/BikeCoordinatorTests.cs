using Sufni.App.Tests.TestSupport.Doubles;
using Microsoft.Extensions.Time.Testing;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Shared.Models;
using NSubstitute;
using Sufni.Kinematics;
using Sufni.Telemetry;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Setups.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Coordinators;

public class BikeCoordinatorTests : IAsyncDisposable
{
    private readonly BikeStore bikeStore = new(new StorePublication(new InlineUiThreadDispatcher()));
    private readonly TestPersistenceHarness database = new($"bike-{Guid.NewGuid():N}",
        new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(9)), inMemory: true);
    private ISynchronizableRepository<Bike> Repository => database.GetRepository<Bike>();
    private readonly IBikeEditorService bikeEditorService = Substitute.For<IBikeEditorService>();
    private readonly TestBikeSaveValidator validator = new();

    public async ValueTask DisposeAsync()
    {
        bikeStore.Dispose();
        await database.DisposeAsync();
    }

    private BikeCoordinator CreateCoordinator(IBikePersistenceTransactionRunner? transactions = null) =>
        new(bikeStore, transactions ?? database.BikeTransactions, bikeEditorService, validator, new InlineBackgroundTaskRunner());

    private async Task SeedAsync(BikeSnapshot snapshot)
    {
        await (await database.GetInitializedConnectionAsync()).InsertOrReplaceAsync(Bike.FromSnapshot(snapshot, snapshot.HasImage ? TestImages.SmallPngBytes() : []));
        await bikeStore.PublishBikesChangedAsync([snapshot]);
    }

    [Fact]
    public async Task StopAsync_WaitsForCutoffPersistenceAndPublication()
    {
        var snapshot = TestSnapshots.Bike();
        await SeedAsync(snapshot);
        var write = new TaskCompletionSource<BikeDampingSpeedCutoffUpdateResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transactions = Substitute.For<IBikePersistenceTransactionRunner>();
        transactions.UpdateCutoffAsync(snapshot.Id, 200, SuspensionType.Front, DampingSpeedCircuit.Compression, 300)
            .Returns(write.Task);
        IBikeCoordinator coordinator = CreateCoordinator(transactions);
        var operation = coordinator.UpdateDampingSpeedCutoffAsync(snapshot.Id, 200,
            SuspensionType.Front, DampingSpeedCircuit.Compression, 300);

        var stop = coordinator.StopAsync();
        Assert.False(stop.IsCompleted);
        Assert.Equal(200, bikeStore.Get(snapshot.Id)!.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
        write.SetResult(new BikeDampingSpeedCutoffUpdateResult.Saved(snapshot with { FrontCompressionDampingCutoffMmPerSecond = 300 }));
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(300, bikeStore.Get(snapshot.Id)!.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
        Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Saved>(await operation.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task LoadedDraft_PreservesImageThroughMetadataAndCutoffSaves()
    {
        var bike = new Bike(Guid.NewGuid(), "pictured") { ImageBytes = [1, 2, 3], HeadAngle = 65, ForkStroke = 160 };
        await (await database.GetInitializedConnectionAsync()).InsertOrReplaceAsync(bike);
        await bikeStore.PublishBikesChangedAsync([BikeSnapshot.From(bike)]);
        var coordinator = CreateCoordinator();
        var loaded = BikeDraft.From((await Repository.GetAsync(bike.Id))!);

        Assert.IsType<BikeSaveResult.Saved>(await coordinator.SaveAsync(loaded with { Name = "renamed" }, loaded));
        Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Saved>(await coordinator.UpdateDampingSpeedCutoffAsync(
            bike.Id, 200, SuspensionType.Front, DampingSpeedCircuit.Compression, 300));

        Assert.Equal([1, 2, 3], (await Repository.GetAsync(bike.Id))!.ImageBytes);
        Assert.True(bikeStore.Get(bike.Id)!.HasImage);
    }

    [Fact]
    public async Task SaveAsync_DetectsChangedImageEvenWhenMetadataAndTimestampMatch()
    {
        var bike = new Bike(Guid.NewGuid(), "pictured") { ImageBytes = [1, 2, 3], HeadAngle = 65, ForkStroke = 160 };
        await (await database.GetInitializedConnectionAsync()).InsertOrReplaceAsync(bike);
        await bikeStore.PublishBikesChangedAsync([BikeSnapshot.From(bike)]);
        var coordinator = CreateCoordinator();
        var baseline = BikeDraft.From((await Repository.GetAsync(bike.Id))!);
        bike.ImageBytes = [4, 5, 6];
        await (await database.GetInitializedConnectionAsync()).UpdateAsync(bike);

        var result = Assert.IsType<BikeSaveResult.Conflict>(await coordinator.SaveAsync(baseline with { Name = "edited" }, baseline));

        Assert.Equal([4, 5, 6], result.CurrentDraft.ImageBytes);
        Assert.Equal("pictured", (await Repository.GetAsync(bike.Id))!.Name);
    }

    [Fact]
    public async Task ImportBikeAsync_NormalizesImportedBikeToNewDraft()
    {
        var importedBike = new Bike(Guid.NewGuid(), "imported")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            FrontWheelRimSize = EtrtoRimSize.Inch29,
            FrontWheelTireWidth = 2.4,
            FrontWheelDiameterMm = 743.9,
            RearWheelRimSize = EtrtoRimSize.Inch275,
            RearWheelTireWidth = 2.5,
            RearWheelDiameterMm = 711,
            ImageRotationDegrees = 12.5,
            Updated = 10,

            Deleted = 1,
        };
        bikeEditorService.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(new BikeFileImportResult.Imported(importedBike));

        var result = await CreateCoordinator().ImportBikeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var imported = Assert.IsType<BikeImportResult.Imported>(result);
        Assert.Equal("imported", imported.Draft.Name);
        Assert.NotEqual(Guid.Empty, imported.Draft.Id);
        Assert.NotEqual(importedBike.Id, imported.Draft.Id);
        Assert.Equal(EtrtoRimSize.Inch29, imported.Draft.FrontWheel!.RimSize);
        Assert.Equal(2.4, imported.Draft.FrontWheel!.TireWidth);
        Assert.Equal(743.9, imported.Draft.FrontWheel!.DiameterMm);
        Assert.Equal(EtrtoRimSize.Inch275, imported.Draft.RearWheel!.RimSize);
        Assert.Equal(2.5, imported.Draft.RearWheel!.TireWidth);
        Assert.Equal(711, imported.Draft.RearWheel!.DiameterMm);
        Assert.Equal(12.5, imported.Draft.ImageRotationDegrees);
    }

    [Fact]
    public async Task ImportBikeAsync_RejectsInvalidDraftWithoutPersisting()
    {
        var importedBike = new Bike(Guid.NewGuid(), "invalid import");
        bikeEditorService.ImportBikeAsync(Arg.Any<CancellationToken>()).Returns(new BikeFileImportResult.Imported(importedBike));
        validator.Evaluate = _ => Task.FromResult<BikeSaveValidation>(new BikeSaveValidation.Invalid("invalid bike"));

        var result = await CreateCoordinator().ImportBikeAsync(TestContext.Current.CancellationToken);

        Assert.IsType<BikeImportResult.InvalidFile>(result);
        Assert.Empty(await Repository.GetAllAsync());
    }

    [Theory]
    [InlineData("geometry")]
    [InlineData("wheel")]
    [InlineData("linkage")]
    [InlineData("stroke")]
    public async Task SaveAsync_RealValidationRejectsInvalidDraftWithoutChangingFacts(string invalidInput)
    {
        var original = TestSnapshots.Bike();
        await SeedAsync(original);
        var draft = BikeDraft.From(original) with { Name = "changed" };
        draft = invalidInput switch
        {
            "geometry" => draft with { HeadAngle = null },
            "wheel" => draft with { FrontWheel = new WheelSpec(760, null, null) },
            "linkage" => draft with { RearSuspension = new RearSuspensionSpec.LinkageDraft() },
            _ => draft with
            {
                ShockStroke = 15,
                RearSuspension = new RearSuspensionSpec.LeverageRatio(TestSnapshots.LeverageRatioCurve((0, 0), (20, 50))),
            },
        };
        IBikeCoordinator coordinator = new BikeCoordinator(bikeStore, database.BikeTransactions,
            bikeEditorService, new BikeSaveValidator(new BikeRearSuspensionValidator(), new KinematicSolutionCache(), new InlineBackgroundTaskRunner()),
            new InlineBackgroundTaskRunner());

        Assert.IsType<BikeSaveResult.Invalid>(await coordinator.SaveAsync(draft, BikeDraft.From(original)));

        Assert.Equal(original, bikeStore.Get(original.Id));
        Assert.Equal(original, BikeSnapshot.From((await Repository.GetAsync(original.Id))!));
    }

    // ----- SaveAsync -----

    [Fact]
    public async Task SaveAsync_HappyPath_CommitsBike_AndReturnsSaved()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        var coordinator = CreateCoordinator();

        var bike = new Bike(existing.Id, "renamed")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            FrontWheelDiameterMm = 760,
            RearWheelDiameterMm = 750,
            ImageRotationDegrees = 13.5,
            Updated = 7,
        };

        var result = await coordinator.SaveAsync(BikeDraft.From(bike), BikeDraft.From(existing));

        Assert.Equal(BikeSnapshot.From((await Repository.GetAsync(existing.Id))!), bikeStore.Get(existing.Id));
        var saved = Assert.IsType<BikeSaveResult.Saved>(result);
        var persisted = (await Repository.GetAsync(existing.Id))!;
        Assert.Equal("renamed", persisted.Name);
        Assert.Equal(760, persisted.FrontWheelDiameterMm);
        Assert.Equal(750, persisted.RearWheelDiameterMm);
        Assert.Equal(13.5, persisted.ImageRotationDegrees);
        Assert.Equal(9, saved.Snapshot.Updated);
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenPersistenceFails()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        var transactions = Substitute.For<IBikePersistenceTransactionRunner>();
        transactions.SaveAsync(Arg.Any<BikeDraft>(), Arg.Any<BikeDraft?>())
            .Returns(Task.FromException<BikeSaveResult>(new IOException()));
        var coordinator = CreateCoordinator(transactions);

        var bike = new Bike(existing.Id, "x") { HeadAngle = 65, ForkStroke = 160 };

        var result = await coordinator.SaveAsync(BikeDraft.From(bike), BikeDraft.From(existing));

        Assert.Equal(BikeSaveFailureReason.Save, Assert.IsType<BikeSaveResult.Failed>(result).Reason);
        Assert.Equal(existing, bikeStore.Get(existing.Id));
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_RoundsClampsCommitsWithoutNavigation()
    {
        var existing = TestSnapshots.Bike(updated: 5) with
        {
            FrontCompressionDampingCutoffMmPerSecond = 120,
            FrontReboundDampingCutoffMmPerSecond = 130,
            RearCompressionDampingCutoffMmPerSecond = 140,
            RearReboundDampingCutoffMmPerSecond = 150,
        };
        await SeedAsync(existing);

        var result = await CreateCoordinator().UpdateDampingSpeedCutoffAsync(
            existing.Id,
            expectedCutoffMmPerSecond: 120,
            SuspensionType.Front,
            DampingSpeedCircuit.Compression,
            237);

        var saved = Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Saved>(result);
        Assert.Equal(240, saved.Snapshot.FrontCompressionDampingCutoffMmPerSecond);
        Assert.Equal(130, saved.Snapshot.FrontReboundDampingCutoffMmPerSecond);
        Assert.Equal(140, saved.Snapshot.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(150, saved.Snapshot.RearReboundDampingCutoffMmPerSecond);
        Assert.Equal(9, saved.Snapshot.Updated);
        Assert.Equal(BikeSnapshot.From((await Repository.GetAsync(existing.Id))!), bikeStore.Get(existing.Id));
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ClampsCommittedValue()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);

        var result = await CreateCoordinator().UpdateDampingSpeedCutoffAsync(
            existing.Id,
            expectedCutoffMmPerSecond: 200,
            SuspensionType.Rear,
            DampingSpeedCircuit.Rebound,
            2500);

        var saved = Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Saved>(result);
        Assert.Equal(DampingSpeedCutoffs.MaximumMmPerSecond, saved.Snapshot.RearReboundDampingCutoffMmPerSecond);
        Assert.Equal(BikeSnapshot.From((await Repository.GetAsync(existing.Id))!), bikeStore.Get(existing.Id));
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ReturnsFailed_WhenPersistenceFails()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        var transactions = Substitute.For<IBikePersistenceTransactionRunner>();
        transactions.UpdateCutoffAsync(Arg.Any<Guid>(), Arg.Any<double>(), Arg.Any<SuspensionType>(), Arg.Any<DampingSpeedCircuit>(), Arg.Any<double>())
            .Returns(Task.FromException<BikeDampingSpeedCutoffUpdateResult>(new IOException()));

        var result = await CreateCoordinator(transactions).UpdateDampingSpeedCutoffAsync(
            existing.Id,
            expectedCutoffMmPerSecond: 200,
            SuspensionType.Front,
            DampingSpeedCircuit.Compression,
            300);

        Assert.Equal(BikeDampingCutoffFailureReason.Save, Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Failed>(result).Reason);
        Assert.Equal(existing, bikeStore.Get(existing.Id));
    }

    [Fact]
    public async Task SaveAsync_ValidationFailureLeavesPersistedAndPublishedBikeUnchanged()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        validator.Evaluate = _ => Task.FromResult<BikeSaveValidation>(new BikeSaveValidation.Invalid("unsolvable linkage"));

        var result = await CreateCoordinator().SaveAsync(BikeDraft.From(existing) with { Name = "new name" }, BikeDraft.From(existing));

        Assert.IsType<BikeSaveResult.Invalid>(result);
        Assert.Equal(existing, BikeSnapshot.From((await Repository.GetAsync(existing.Id))!));
        Assert.Equal(existing, bikeStore.Get(existing.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAsync_ChecksForConflictsAfterAnalysisBeforePersisting(bool changedDuringAnalysis)
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        var analysisCompletion = new TaskCompletionSource<BikeSaveValidation>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        validator.Evaluate = _ => analysisCompletion.Task;
        var coordinator = CreateCoordinator();
        var linkage = TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);
        var bike = new Bike(existing.Id, "full sus")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            ShockStroke = 50,
            PixelsToMillimeters = 1,
            ImageBytes = TestImages.SmallPngBytes(),
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
        };

        var saveTask = coordinator.SaveAsync(BikeDraft.From(bike), BikeDraft.From(existing));

        Assert.False(saveTask.IsCompleted);
        Assert.Equal(BikeSnapshot.From((await Repository.GetAsync(existing.Id))!), existing);

        var concurrent = existing with { Name = "concurrent edit", Updated = 10 };
        if (changedDuringAnalysis) await SeedAsync(concurrent);
        analysisCompletion.SetResult(new BikeSaveValidation.Valid());
        var result = await saveTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        if (changedDuringAnalysis)
        {
            Assert.Equal(BikeDraft.From(concurrent), Assert.IsType<BikeSaveResult.Conflict>(result).CurrentDraft);
            Assert.Equal(concurrent, BikeSnapshot.From((await Repository.GetAsync(existing.Id))!));
        }
        else
        {
            Assert.IsType<BikeSaveResult.Saved>(result);
            Assert.Equal("full sus", (await Repository.GetAsync(existing.Id))!.Name);
        }
        Assert.Equal(BikeSnapshot.From((await Repository.GetAsync(existing.Id))!), bikeStore.Get(existing.Id));
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_ReturnsInUse_WhenPersistedSetupReferencesBike()
    {
        var initial = TestSnapshots.Bike(updated: 5);
        await SeedAsync(initial);
        var id = initial.Id;
        await (await database.GetInitializedConnectionAsync()).InsertOrReplaceAsync(new Setup(Guid.NewGuid(), "setup") { BikeId = id });
        var coordinator = CreateCoordinator();

        var result = await coordinator.DeleteAsync(id);

        Assert.IsType<BikeDeleteResult.InUse>(result);
        Assert.NotNull(await Repository.GetAsync(id));
        Assert.Equal(initial, bikeStore.Get(id));
    }

    [Fact]
    public async Task DeleteAsync_RemovesPersistedAndPublishedBike()
    {
        var initial = TestSnapshots.Bike(updated: 5);
        await SeedAsync(initial);
        var id = initial.Id;
        var coordinator = CreateCoordinator();

        var result = await coordinator.DeleteAsync(id);

        Assert.IsType<BikeDeleteResult.Deleted>(result);
        Assert.Null(await Repository.GetAsync(id));
        Assert.Null(bikeStore.Get(id));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailed_WhenPersistenceFails_AndKeepsPublishedBike()
    {
        var initial = TestSnapshots.Bike(updated: 5);
        await SeedAsync(initial);
        var id = initial.Id;
        var transactions = Substitute.For<IBikePersistenceTransactionRunner>();
        transactions.DeleteBikeAsync(Arg.Any<Guid>())
            .Returns(Task.FromException<BikeDeletePersistenceResult>(new IOException()));
        var coordinator = CreateCoordinator(transactions);

        var result = await coordinator.DeleteAsync(id);

        Assert.Equal(BikeDeleteFailureReason.Delete, Assert.IsType<BikeDeleteResult.Failed>(result).Reason);
        Assert.NotNull(await Repository.GetAsync(id));
        Assert.Equal(initial, bikeStore.Get(id));
    }

}

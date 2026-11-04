using Sufni.App.Tests.TestSupport.Doubles;
using Microsoft.Extensions.Time.Testing;
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

public class BikeCoordinatorTests : IDisposable
{
    private readonly BikeStore bikeStore = new(new StorePublication(new InlineUiThreadDispatcher()));
    private readonly InMemorySynchronizableRepository<Bike> repository = new(new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(9)));
    private readonly InMemorySynchronizableRepository<Setup> setups = new(new FakeTimeProvider(DateTimeOffset.FromUnixTimeSeconds(9)));
    private readonly IBikeEditorService bikeEditorService = Substitute.For<IBikeEditorService>();
    private readonly TestBikeSaveValidator validator = new();

    public void Dispose() => bikeStore.Dispose();

    private BikeCoordinator CreateCoordinator() =>
        new(bikeStore, new InMemoryBikeTransactions(repository, setups), bikeEditorService, validator, repository, new InlineBackgroundTaskRunner());

    private async Task SeedAsync(BikeSnapshot snapshot)
    {
        repository.Seed(Bike.FromSnapshot(snapshot, snapshot.HasImage ? TestImages.SmallPngBytes() : []));
        await bikeStore.PublishBikesChangedAsync([snapshot]);
    }

    [Fact]
    public async Task StopAsync_WaitsForCutoffPersistenceAndPublication()
    {
        var snapshot = TestSnapshots.Bike();
        await SeedAsync(snapshot);
        var write = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.BeforeWrite = () => write.Task;
        IBikeCoordinator coordinator = CreateCoordinator();
        var operation = coordinator.UpdateDampingSpeedCutoffAsync(snapshot.Id, 200,
            SuspensionType.Front, DampingSpeedCircuit.Compression, 300);

        var stop = coordinator.StopAsync();
        Assert.False(stop.IsCompleted);
        Assert.Equal(200, bikeStore.Get(snapshot.Id)!.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
        write.SetResult();
        await stop.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(300, bikeStore.Get(snapshot.Id)!.DampingSpeedCutoffs.Front.CompressionMmPerSecond);
        Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Saved>(await operation.AwaitBoundedAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task LoadedDraft_PreservesImageThroughMetadataAndCutoffSaves()
    {
        var bike = new Bike(Guid.NewGuid(), "pictured") { ImageBytes = [1, 2, 3], HeadAngle = 65, ForkStroke = 160 };
        repository.Seed(bike);
        await bikeStore.PublishBikesChangedAsync([BikeSnapshot.From(bike)]);
        var coordinator = CreateCoordinator();
        var loaded = Assert.IsType<BikeDraftLoadResult.Loaded>(await coordinator.LoadDraftAsync(bike.Id, TestContext.Current.CancellationToken));

        Assert.Equal([1, 2, 3], loaded.Draft.ImageBytes);
        Assert.IsType<BikeSaveResult.Saved>(await coordinator.SaveAsync(loaded.Draft with { Name = "renamed" }, loaded.Draft));
        Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Saved>(await coordinator.UpdateDampingSpeedCutoffAsync(
            bike.Id, 200, SuspensionType.Front, DampingSpeedCircuit.Compression, 300));

        Assert.Equal([1, 2, 3], (await repository.GetAsync(bike.Id))!.ImageBytes);
        Assert.True(bikeStore.Get(bike.Id)!.HasImage);
    }

    [Fact]
    public async Task SaveAsync_DetectsChangedImageEvenWhenMetadataAndTimestampMatch()
    {
        var bike = new Bike(Guid.NewGuid(), "pictured") { ImageBytes = [1, 2, 3], HeadAngle = 65, ForkStroke = 160 };
        repository.Seed(bike);
        await bikeStore.PublishBikesChangedAsync([BikeSnapshot.From(bike)]);
        var coordinator = CreateCoordinator();
        var baseline = Assert.IsType<BikeDraftLoadResult.Loaded>(await coordinator.LoadDraftAsync(bike.Id, TestContext.Current.CancellationToken)).Draft;
        bike.ImageBytes = [4, 5, 6];

        var result = Assert.IsType<BikeSaveResult.Conflict>(await coordinator.SaveAsync(baseline with { Name = "edited" }, baseline));

        Assert.Equal([4, 5, 6], result.CurrentDraft.ImageBytes);
        Assert.Equal("pictured", (await repository.GetAsync(bike.Id))!.Name);
    }

    [Fact]
    public async Task LoadDraftAsync_ReturnsMissingForDeletedBike()
    {
        var bike = new Bike(Guid.NewGuid(), "deleted") { Deleted = 1, ImageBytes = [1] };
        repository.Seed(bike);
        Assert.IsType<BikeDraftLoadResult.Missing>(await CreateCoordinator().LoadDraftAsync(bike.Id, TestContext.Current.CancellationToken));
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
        bikeEditorService.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(new BikeEditorAnalysisResult.NotApplicable());

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
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task SaveAsync_IgnoresTimestampAndCutoffChanges_AndPreservesCurrentCutoffs()
    {
        var baseline = TestSnapshots.Bike(updated: 5);
        var cutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400);
        var current = baseline with { Updated = 20, DampingSpeedCutoffs = cutoffs };
        await SeedAsync(current);
        var draft = BikeDraft.From(baseline) with { Name = "edited" };

        var result = Assert.IsType<BikeSaveResult.Saved>(await CreateCoordinator().SaveAsync(draft, BikeDraft.From(baseline)));

        Assert.Equal("edited", result.Snapshot.Name);
        Assert.Equal(cutoffs, result.Snapshot.DampingSpeedCutoffs);
        Assert.Equal(cutoffs, (await repository.GetAsync(baseline.Id))!.DampingSpeedCutoffs);
    }

    [Fact]
    public async Task SaveAsync_NewDraftPreservesImportedCutoffs()
    {
        var draft = BikeDraft.From(TestSnapshots.Bike()) with { InitialDampingSpeedCutoffs = DampingSpeedCutoffs.FromValues(100, 200, 300, 400) };

        var saved = Assert.IsType<BikeSaveResult.Saved>(await CreateCoordinator().SaveAsync(draft, null));

        Assert.Equal(draft.InitialDampingSpeedCutoffs, saved.Snapshot.DampingSpeedCutoffs);
        Assert.Equal(draft.InitialDampingSpeedCutoffs, (await repository.GetAsync(draft.Id))!.DampingSpeedCutoffs);
    }

    [Fact]
    public async Task SaveAsync_DoesNotRecreateRemovedExistingBike()
    {
        var original = TestSnapshots.Bike();
        await SeedAsync(original);
        await repository.DeleteAsync(original.Id);
        await bikeStore.PublishBikesRemovedAsync([original.Id]);

        var result = await CreateCoordinator().SaveAsync(BikeDraft.From(original) with { Name = "later" }, BikeDraft.From(original));

        Assert.IsType<BikeSaveResult.Missing>(result);
        Assert.Null(await repository.GetAsync(original.Id));
        Assert.Null(bikeStore.Get(original.Id));
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
        IBikeCoordinator coordinator = new BikeCoordinator(bikeStore, new InMemoryBikeTransactions(repository, setups),
            bikeEditorService, new BikeSaveValidator(new BikeRearSuspensionValidator(), new KinematicSolutionCache(), new InlineBackgroundTaskRunner()),
            repository, new InlineBackgroundTaskRunner());

        Assert.IsType<BikeSaveResult.Invalid>(await coordinator.SaveAsync(draft, BikeDraft.From(original)));

        Assert.Equal(original, bikeStore.Get(original.Id));
        Assert.Equal(original, BikeSnapshot.From((await repository.GetAsync(original.Id))!));
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

        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(existing.Id))!), bikeStore.Get(existing.Id));
        var saved = Assert.IsType<BikeSaveResult.Saved>(result);
        var persisted = (await repository.GetAsync(existing.Id))!;
        Assert.Equal("renamed", persisted.Name);
        Assert.Equal(760, persisted.FrontWheelDiameterMm);
        Assert.Equal(750, persisted.RearWheelDiameterMm);
        Assert.Equal(13.5, persisted.ImageRotationDegrees);
        Assert.Equal(9, saved.Snapshot.Updated);
    }

    [Fact]
    public async Task SaveAsync_ReturnsConflict_WhenAuthoredFieldsChangedAtSameTimestamp_AndDoesNotPersist()
    {
        var current = TestSnapshots.Bike(updated: 10);
        await SeedAsync(current);
        var coordinator = CreateCoordinator();

        var bike = new Bike(current.Id, "stale edit") { HeadAngle = 65, ForkStroke = 160 };

        var result = await coordinator.SaveAsync(BikeDraft.From(bike), BikeDraft.From(current with { Name = "original name" }));

        var conflict = Assert.IsType<BikeSaveResult.Conflict>(result);
        Assert.Equal(BikeDraft.From(current), conflict.CurrentDraft);
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(current.Id))!), current);
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenPersistenceFails()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        repository.WriteFailure = new IOException("disk full");
        var coordinator = CreateCoordinator();

        var bike = new Bike(existing.Id, "x") { HeadAngle = 65, ForkStroke = 160 };

        var result = await coordinator.SaveAsync(BikeDraft.From(bike), BikeDraft.From(existing));

        Assert.Equal(BikeSaveFailureReason.Save, Assert.IsType<BikeSaveResult.Failed>(result).Reason);
        Assert.Equal(existing, bikeStore.Get(existing.Id));
        Assert.Equal(existing, BikeSnapshot.From((await repository.GetAsync(existing.Id))!));
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ReturnsMissing_WhenBikeIsGone()
    {
        var result = await CreateCoordinator().UpdateDampingSpeedCutoffAsync(
            Guid.NewGuid(), 5, SuspensionType.Front, DampingSpeedCircuit.Compression, 300);

        Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Missing>(result);
        Assert.Empty(await repository.GetAllAsync());
    }

    // ----- UpdateDampingSpeedCutoffAsync -----

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
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(existing.Id))!), bikeStore.Get(existing.Id));
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
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(existing.Id))!), bikeStore.Get(existing.Id));
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ReturnsConflict_WhenTargetCutoffChangedAtSameTimestamp()
    {
        var current = TestSnapshots.Bike(updated: 5) with { FrontReboundDampingCutoffMmPerSecond = 240 };
        await SeedAsync(current);

        var result = await CreateCoordinator().UpdateDampingSpeedCutoffAsync(
            current.Id,
            expectedCutoffMmPerSecond: 200,
            SuspensionType.Front,
            DampingSpeedCircuit.Rebound,
            300);

        var conflict = Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Conflict>(result);
        Assert.Equal(current, conflict.CurrentSnapshot);
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(current.Id))!), current);
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_PreservesUnrelatedMetadataAndCutoffChanges()
    {
        var current = TestSnapshots.Bike(name: "Renamed elsewhere", updated: 100) with
        {
            RearCompressionDampingCutoffMmPerSecond = 450,
        };
        await SeedAsync(current);

        var result = await CreateCoordinator().UpdateDampingSpeedCutoffAsync(
            current.Id, 200, SuspensionType.Front, DampingSpeedCircuit.Rebound, 310);

        var saved = Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Saved>(result);
        Assert.Equal(current.Name, saved.Snapshot.Name);
        Assert.Equal(450, saved.Snapshot.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(310, saved.Snapshot.FrontReboundDampingCutoffMmPerSecond);
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ReturnsFailed_WhenPersistenceFails()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        repository.WriteFailure = new IOException("disk full");

        var result = await CreateCoordinator().UpdateDampingSpeedCutoffAsync(
            existing.Id,
            expectedCutoffMmPerSecond: 200,
            SuspensionType.Front,
            DampingSpeedCircuit.Compression,
            300);

        Assert.Equal(BikeDampingCutoffFailureReason.Save, Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Failed>(result).Reason);
        Assert.Equal(existing, bikeStore.Get(existing.Id));
        Assert.Equal(existing, BikeSnapshot.From((await repository.GetAsync(existing.Id))!));
    }

    [Fact]
    public async Task SaveAsync_ValidationFailureLeavesPersistedAndPublishedBikeUnchanged()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        validator.Evaluate = _ => Task.FromResult<BikeSaveValidation>(new BikeSaveValidation.Invalid("unsolvable linkage"));

        var result = await CreateCoordinator().SaveAsync(BikeDraft.From(existing) with { Name = "new name" }, BikeDraft.From(existing));

        Assert.IsType<BikeSaveResult.Invalid>(result);
        Assert.Equal(existing, BikeSnapshot.From((await repository.GetAsync(existing.Id))!));
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
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(existing.Id))!), existing);

        var concurrent = existing with { Name = "concurrent edit", Updated = 10 };
        if (changedDuringAnalysis) await SeedAsync(concurrent);
        analysisCompletion.SetResult(new BikeSaveValidation.Valid());
        var result = await saveTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        if (changedDuringAnalysis)
        {
            Assert.Equal(BikeDraft.From(concurrent), Assert.IsType<BikeSaveResult.Conflict>(result).CurrentDraft);
            Assert.Equal(concurrent, BikeSnapshot.From((await repository.GetAsync(existing.Id))!));
        }
        else
        {
            Assert.IsType<BikeSaveResult.Saved>(result);
            Assert.Equal("full sus", (await repository.GetAsync(existing.Id))!.Name);
        }
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(existing.Id))!), bikeStore.Get(existing.Id));
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_ReturnsInUse_WhenPersistedSetupReferencesBike()
    {
        var initial = TestSnapshots.Bike(updated: 5);
        await SeedAsync(initial);
        var id = initial.Id;
        setups.Seed(new Setup(Guid.NewGuid(), "setup") { BikeId = id });
        var coordinator = CreateCoordinator();

        var result = await coordinator.DeleteAsync(id);

        Assert.IsType<BikeDeleteResult.InUse>(result);
        Assert.NotNull(await repository.GetAsync(id));
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
        Assert.Null(await repository.GetAsync(id));
        Assert.Null(bikeStore.Get(id));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailed_WhenPersistenceFails_AndKeepsPublishedBike()
    {
        var initial = TestSnapshots.Bike(updated: 5);
        await SeedAsync(initial);
        var id = initial.Id;
        repository.WriteFailure = new IOException("locked");
        var coordinator = CreateCoordinator();

        var result = await coordinator.DeleteAsync(id);

        Assert.Equal(BikeDeleteFailureReason.Delete, Assert.IsType<BikeDeleteResult.Failed>(result).Reason);
        Assert.NotNull(await repository.GetAsync(id));
        Assert.Equal(initial, bikeStore.Get(id));
    }

}

using Microsoft.Extensions.Time.Testing;
using Sufni.App.Tests.TestSupport.Persistence;
using Sufni.App.Shared.Models;
using NSubstitute;
using Sufni.Kinematics;
using Sufni.Telemetry;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
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
    private readonly IBikeDependencyQuery dependencyQuery = Substitute.For<IBikeDependencyQuery>();
    private readonly IBikeEditorService bikeEditorService = Substitute.For<IBikeEditorService>();
    private readonly IBikeRearSuspensionValidator rearSuspensionValidator = new BikeRearSuspensionValidator();

    public void Dispose() => bikeStore.Dispose();

    private BikeCoordinator CreateCoordinator() =>
        new(bikeStore, dependencyQuery, bikeEditorService, rearSuspensionValidator, repository, new InlineBackgroundTaskRunner());

    private async Task SeedAsync(BikeSnapshot snapshot)
    {
        repository.Seed(Bike.FromSnapshot(snapshot));
        await bikeStore.PublishBikesChangedAsync([snapshot]);
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
            ClientUpdated = 5,
            Deleted = 1,
        };
        bikeEditorService.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(new BikeFileImportResult.Imported(importedBike));
        bikeEditorService.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(new BikeEditorAnalysisResult.NotApplicable());

        var result = await CreateCoordinator().ImportBikeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var imported = Assert.IsType<BikeImportResult.Imported>(result);
        Assert.Equal("imported", imported.Data.Bike.Name);
        Assert.NotEqual(Guid.Empty, imported.Data.Bike.Id);
        Assert.NotEqual(importedBike.Id, imported.Data.Bike.Id);
        Assert.Equal(0, imported.Data.Bike.Updated);
        Assert.Equal(0, imported.Data.Bike.ClientUpdated);
        Assert.Null(imported.Data.Bike.Deleted);
        Assert.Equal(EtrtoRimSize.Inch29, imported.Data.Bike.FrontWheelRimSize);
        Assert.Equal(2.4, imported.Data.Bike.FrontWheelTireWidth);
        Assert.Equal(743.9, imported.Data.Bike.FrontWheelDiameterMm);
        Assert.Equal(EtrtoRimSize.Inch275, imported.Data.Bike.RearWheelRimSize);
        Assert.Equal(2.5, imported.Data.Bike.RearWheelTireWidth);
        Assert.Equal(711, imported.Data.Bike.RearWheelDiameterMm);
        Assert.Equal(12.5, imported.Data.Bike.ImageRotationDegrees);
        Assert.IsType<BikeEditorAnalysisResult.NotApplicable>(imported.Data.AnalysisResult);
    }

    [Fact]
    public async Task ImportBikeAsync_LoadsUnsolvableAnalysis_ForDraftRearSuspension()
    {
        var importedBike = new Bike(Guid.NewGuid(), "mismatched import")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            RearSuspension = new RearSuspensionSpec.LeverageRatioDraft(),
        };
        bikeEditorService.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(new BikeFileImportResult.Imported(importedBike));
        bikeEditorService.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(new BikeEditorAnalysisResult.Unsolvable());

        var result = await CreateCoordinator().ImportBikeAsync(cancellationToken: TestContext.Current.CancellationToken);

        var imported = Assert.IsType<BikeImportResult.Imported>(result);
        Assert.IsType<BikeEditorAnalysisResult.Unsolvable>(imported.Data.AnalysisResult);
        await bikeEditorService.Received(1)
            .LoadAnalysisAsync(Arg.Is<RearSuspensionSpec.LeverageRatioDraft>(_ => true), Arg.Any<CancellationToken>());
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

        var result = await coordinator.SaveAsync(bike, baselineUpdated: 5);

        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(existing.Id))!), bikeStore.Get(existing.Id));
        var saved = Assert.IsType<BikeSaveResult.Saved>(result);
        var persisted = (await repository.GetAsync(existing.Id))!;
        Assert.Equal("renamed", persisted.Name);
        Assert.Equal(760, persisted.FrontWheelDiameterMm);
        Assert.Equal(750, persisted.RearWheelDiameterMm);
        Assert.Equal(13.5, persisted.ImageRotationDegrees);
        Assert.Equal(9, saved.NewBaselineUpdated);
        Assert.IsType<BikeEditorAnalysisResult.NotApplicable>(saved.AnalysisResult);
    }

    [Fact]
    public async Task SaveAsync_ReturnsConflict_WhenStoreIsNewer_AndDoesNotPersist()
    {
        var current = TestSnapshots.Bike(updated: 10);
        await SeedAsync(current);
        var coordinator = CreateCoordinator();

        var bike = new Bike(current.Id, "stale edit") { HeadAngle = 65, ForkStroke = 160 };

        var result = await coordinator.SaveAsync(bike, baselineUpdated: 5);

        var conflict = Assert.IsType<BikeSaveResult.Conflict>(result);
        Assert.Same(current, conflict.CurrentSnapshot);
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

        var result = await coordinator.SaveAsync(bike, baselineUpdated: 5);

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
            baselineUpdated: 5,
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
            baselineUpdated: 5,
            SuspensionType.Rear,
            DampingSpeedCircuit.Rebound,
            2500);

        var saved = Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Saved>(result);
        Assert.Equal(DampingSpeedCutoffs.MaximumMmPerSecond, saved.Snapshot.RearReboundDampingCutoffMmPerSecond);
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(existing.Id))!), bikeStore.Get(existing.Id));
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ReturnsConflict_WhenStoreIsNewer()
    {
        var current = TestSnapshots.Bike(updated: 10);
        await SeedAsync(current);

        var result = await CreateCoordinator().UpdateDampingSpeedCutoffAsync(
            current.Id,
            baselineUpdated: 5,
            SuspensionType.Front,
            DampingSpeedCircuit.Rebound,
            300);

        var conflict = Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Conflict>(result);
        Assert.Same(current, conflict.CurrentSnapshot);
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(current.Id))!), current);
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ReturnsFailed_WhenPersistenceFails()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        repository.WriteFailure = new IOException("disk full");

        var result = await CreateCoordinator().UpdateDampingSpeedCutoffAsync(
            existing.Id,
            baselineUpdated: 5,
            SuspensionType.Front,
            DampingSpeedCircuit.Compression,
            300);

        Assert.Equal(BikeDampingCutoffFailureReason.Save, Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Failed>(result).Reason);
        Assert.Equal(existing, bikeStore.Get(existing.Id));
        Assert.Equal(existing, BikeSnapshot.From((await repository.GetAsync(existing.Id))!));
    }

    [Fact]
    public async Task SaveAsync_ReturnsInvalidRearSuspension_WhenLinkageAnalysisIsUnsolvable()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        bikeEditorService.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(new BikeEditorAnalysisResult.Unsolvable());
        var coordinator = CreateCoordinator();

        var bike = new Bike(existing.Id, "full sus")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            ShockStroke = 50,
            Chainstay = 440,
            PixelsToMillimeters = 1,
            ImageBytes = TestImages.SmallPngBytes(),
            RearSuspension = new RearSuspensionSpec.Linkage(
                TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true)),
        };

        var result = await coordinator.SaveAsync(bike, baselineUpdated: 5);

        var invalid = Assert.IsType<BikeSaveResult.InvalidRearSuspension>(result);
        Assert.False(string.IsNullOrWhiteSpace(invalid.ErrorMessage));
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(existing.Id))!), existing);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveAsync_ChecksForConflictsAfterAnalysisBeforePersisting(bool changedDuringAnalysis)
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        var analysisCompletion = new TaskCompletionSource<BikeEditorAnalysisResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bikeEditorService.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(_ => analysisCompletion.Task);
        var coordinator = CreateCoordinator();
        var linkage = TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);
        var bike = new Bike(existing.Id, "full sus")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            ShockStroke = 50,
            Chainstay = 440,
            PixelsToMillimeters = 1,
            ImageBytes = TestImages.SmallPngBytes(),
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
        };

        var saveTask = coordinator.SaveAsync(bike, baselineUpdated: 5);

        _ = bikeEditorService.Received(1)
            .LoadAnalysisAsync(
                Arg.Is<RearSuspensionSpec.Linkage>(rearSuspension => rearSuspension.Spec == linkage),
                Arg.Any<CancellationToken>());
        Assert.False(saveTask.IsCompleted);
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(existing.Id))!), existing);

        var analysisResult = new BikeEditorAnalysisResult.Computed(
            new BikeAnalysisPresentationData(
                new CoordinateList([0], [0]),
                null));
        var concurrent = existing with { Name = "concurrent edit", Updated = 10 };
        if (changedDuringAnalysis) await SeedAsync(concurrent);
        analysisCompletion.SetResult(analysisResult);
        var result = await saveTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        if (changedDuringAnalysis)
        {
            Assert.Equal(concurrent, Assert.IsType<BikeSaveResult.Conflict>(result).CurrentSnapshot);
            Assert.Equal(concurrent, BikeSnapshot.From((await repository.GetAsync(existing.Id))!));
        }
        else
        {
            Assert.Same(analysisResult, Assert.IsType<BikeSaveResult.Saved>(result).AnalysisResult);
            Assert.Equal("full sus", (await repository.GetAsync(existing.Id))!.Name);
        }
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(existing.Id))!), bikeStore.Get(existing.Id));
    }

    [Fact]
    public async Task SaveAsync_ReturnsInvalidRearSuspension_WhenLeverageRatioShockStrokeDoesNotMatchCurveMax()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        await SeedAsync(existing);
        var coordinator = CreateCoordinator();

        var bike = Bike.FromSnapshot(TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)),
            shockStroke: 15,
            id: existing.Id,
            updated: 7));

        var result = await coordinator.SaveAsync(bike, baselineUpdated: 5);

        Assert.IsType<BikeSaveResult.InvalidRearSuspension>(result);
        await bikeEditorService.DidNotReceiveWithAnyArgs().LoadAnalysisAsync(
            default!,
            Arg.Any<CancellationToken>());
        Assert.Equal(BikeSnapshot.From((await repository.GetAsync(existing.Id))!), existing);
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_ReturnsInUse_WhenDependencyQueryReportsInUse()
    {
        var initial = TestSnapshots.Bike(updated: 5);
        await SeedAsync(initial);
        var id = initial.Id;
        dependencyQuery.IsBikeInUseAsync(id).Returns(true);
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
        dependencyQuery.IsBikeInUseAsync(id).Returns(false);
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
        dependencyQuery.IsBikeInUseAsync(id).Returns(false);
        repository.WriteFailure = new IOException("locked");
        var coordinator = CreateCoordinator();

        var result = await coordinator.DeleteAsync(id);

        Assert.Equal(BikeDeleteFailureReason.Delete, Assert.IsType<BikeDeleteResult.Failed>(result).Reason);
        Assert.NotNull(await repository.GetAsync(id));
        Assert.Equal(initial, bikeStore.Get(id));
    }

}

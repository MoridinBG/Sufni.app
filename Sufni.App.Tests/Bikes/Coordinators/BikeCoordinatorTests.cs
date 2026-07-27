using Avalonia.Headless.XUnit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.Kinematics;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Shared.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Coordinators;

public class BikeCoordinatorTests
{
    private readonly IBikeStoreWriter bikeStore = Substitute.For<IBikeStoreWriter>();
    private readonly IBikeDependencyQuery dependencyQuery = Substitute.For<IBikeDependencyQuery>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IBikeEditorService bikeEditorService = Substitute.For<IBikeEditorService>();
    private readonly IBikeRearSuspensionValidator rearSuspensionValidator = new BikeRearSuspensionValidator();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly IUiThreadDispatcher uiThreadDispatcher = new InlineUiThreadDispatcher();
    private readonly IEditorFactory editorFactory = Substitute.For<IEditorFactory>();

    public BikeCoordinatorTests()
    {
        editorFactory.CloseBikeEditor(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        bikeStore.CommitBikeAsync(Arg.Any<Bike>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var bike = callInfo.Arg<Bike>();
                return Task.FromResult<StoreMutationResult<BikeSnapshot>>(
                    new StoreMutationResult<BikeSnapshot>.Saved(BikeSnapshot.From(bike)));
            });
        bikeStore.CommitBikeDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoreDeleteResult<BikeSnapshot>>(
                new StoreDeleteResult<BikeSnapshot>.Deleted()));
    }

    private BikeCoordinator CreateCoordinator(UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace)
    {
        BikeCoordinator? coordinator = null;
        coordinator = new(
            bikeStore,
            dependencyQuery,
            shell,
            CreateEnvironment(layoutProfile),
            bikeEditorService,
            rearSuspensionValidator,
            () => editorFactory);
        return coordinator;
    }

    // ----- OpenCreateAsync -----

    [Fact]
    public async Task OpenCreateAsync_OpensNewEditor_ThroughFactory()
    {
        var coordinator = CreateCoordinator();
        await coordinator.OpenCreateAsync();

        editorFactory.Received(1).OpenNewBikeEditor(Arg.Is<BikeSnapshot>(snapshot =>
            snapshot.Name == "new bike"));
    }

    // ----- OpenEditAsync -----

    [Fact]
    public async Task OpenEditAsync_NoOp_WhenSnapshotMissing()
    {
        bikeStore.Get(Arg.Any<Guid>()).Returns((BikeSnapshot?)null);
        var coordinator = CreateCoordinator();

        await coordinator.OpenEditAsync(Guid.NewGuid());

        editorFactory.DidNotReceive().OpenBikeEditor(Arg.Any<BikeSnapshot>());
        editorFactory.DidNotReceive().OpenNewBikeEditor(Arg.Any<BikeSnapshot>());
    }

    [Fact]
    public async Task OpenEditAsync_OpensEditor_ThroughFactory()
    {
        var snapshot = TestSnapshots.Bike();
        bikeStore.Get(snapshot.Id).Returns(snapshot);
        var coordinator = CreateCoordinator();

        await coordinator.OpenEditAsync(snapshot.Id);

        editorFactory.Received(1).OpenBikeEditor(snapshot);
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
            .Returns(new BikeEditorAnalysisResult.Unavailable());

        var result = await CreateCoordinator().ImportBikeAsync();

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
        Assert.IsType<BikeEditorAnalysisResult.Unavailable>(imported.Data.AnalysisResult);
    }

    [Fact]
    public async Task ImportBikeAsync_LoadsUnavailableAnalysis_ForDraftRearSuspension()
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
            .Returns(new BikeEditorAnalysisResult.Unavailable());

        var result = await CreateCoordinator().ImportBikeAsync();

        var imported = Assert.IsType<BikeImportResult.Imported>(result);
        Assert.IsType<BikeEditorAnalysisResult.Unavailable>(imported.Data.AnalysisResult);
        await bikeEditorService.Received(1)
            .LoadAnalysisAsync(Arg.Is<RearSuspensionSpec.LeverageRatioDraft>(_ => true), Arg.Any<CancellationToken>());
    }

    // ----- SaveAsync -----

    [Fact]
    public async Task SaveAsync_HappyPath_CommitsBike_AndReturnsSaved()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        bikeStore.Get(existing.Id).Returns(existing);
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

        await bikeStore.Received(1).CommitBikeAsync(
            Arg.Is<Bike>(b =>
                b.Id == existing.Id &&
                b.Name == "renamed" &&
                b.FrontWheelDiameterMm == 760 &&
                b.RearWheelDiameterMm == 750 &&
                b.ImageRotationDegrees == 13.5 &&
                b.Updated == 7),
            5,
            Arg.Any<CancellationToken>());
        shell.DidNotReceive().GoBack();
        var saved = Assert.IsType<BikeSaveResult.Saved>(result);
        Assert.Equal(7, saved.NewBaselineUpdated);
        Assert.IsType<BikeEditorAnalysisResult.Unavailable>(saved.AnalysisResult);
    }

    [Fact]
    public async Task SaveAsync_OnCompact_NavigatesBackAfterSave()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        bikeStore.Get(existing.Id).Returns(existing);
        var coordinator = CreateCoordinator(UiLayoutProfile.Compact);

        var bike = new Bike(existing.Id, "renamed")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            FrontWheelDiameterMm = 760,
            RearWheelDiameterMm = 750,
            Updated = 7,
        };

        await coordinator.SaveAsync(bike, baselineUpdated: 5);

        shell.Received(1).GoBack();
    }

    [Fact]
    public async Task SaveAsync_ReturnsConflict_WhenStoreIsNewer_AndDoesNotPersist()
    {
        var current = TestSnapshots.Bike(updated: 10);
        bikeStore.Get(current.Id).Returns(current);
        var coordinator = CreateCoordinator();

        var bike = new Bike(current.Id, "stale edit") { HeadAngle = 65, ForkStroke = 160 };

        var result = await coordinator.SaveAsync(bike, baselineUpdated: 5);

        var conflict = Assert.IsType<BikeSaveResult.Conflict>(result);
        Assert.Same(current, conflict.CurrentSnapshot);
        await bikeStore.DidNotReceive().CommitBikeAsync(Arg.Any<Bike>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenStoreCommitFails()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        bikeStore.Get(existing.Id).Returns(existing);
        bikeStore.CommitBikeAsync(Arg.Any<Bike>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoreMutationResult<BikeSnapshot>>(
                new StoreMutationResult<BikeSnapshot>.Failed("disk full")));
        var coordinator = CreateCoordinator();

        var bike = new Bike(existing.Id, "x") { HeadAngle = 65, ForkStroke = 160 };

        var result = await coordinator.SaveAsync(bike, baselineUpdated: 5);

        Assert.IsType<BikeSaveResult.Failed>(result);
        shell.DidNotReceive().GoBack();
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
        bikeStore.Get(existing.Id).Returns(existing);
        bikeStore.CommitBikeAsync(Arg.Any<Bike>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var bike = callInfo.Arg<Bike>();
                bike.Updated = 9;
                return Task.FromResult<StoreMutationResult<BikeSnapshot>>(
                    new StoreMutationResult<BikeSnapshot>.Saved(BikeSnapshot.From(bike)));
            });

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
        await bikeStore.Received(1).CommitBikeAsync(
            Arg.Is<Bike>(bike =>
                bike.Id == existing.Id &&
                bike.FrontCompressionDampingCutoffMmPerSecond == 240 &&
                bike.FrontReboundDampingCutoffMmPerSecond == 130 &&
                bike.RearCompressionDampingCutoffMmPerSecond == 140 &&
                bike.RearReboundDampingCutoffMmPerSecond == 150),
            5,
            Arg.Any<CancellationToken>());
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ClampsCommittedValue()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        bikeStore.Get(existing.Id).Returns(existing);

        var result = await CreateCoordinator().UpdateDampingSpeedCutoffAsync(
            existing.Id,
            baselineUpdated: 5,
            SuspensionType.Rear,
            DampingSpeedCircuit.Rebound,
            2500);

        var saved = Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Saved>(result);
        Assert.Equal(DampingSpeedCutoffs.MaximumMmPerSecond, saved.Snapshot.RearReboundDampingCutoffMmPerSecond);
        await bikeStore.Received(1).CommitBikeAsync(
            Arg.Is<Bike>(bike =>
                bike.RearReboundDampingCutoffMmPerSecond == DampingSpeedCutoffs.MaximumMmPerSecond),
            5,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ReturnsConflict_WhenStoreIsNewer()
    {
        var current = TestSnapshots.Bike(updated: 10);
        bikeStore.Get(current.Id).Returns(current);

        var result = await CreateCoordinator().UpdateDampingSpeedCutoffAsync(
            current.Id,
            baselineUpdated: 5,
            SuspensionType.Front,
            DampingSpeedCircuit.Rebound,
            300);

        var conflict = Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Conflict>(result);
        Assert.Same(current, conflict.CurrentSnapshot);
        await bikeStore.DidNotReceive().CommitBikeAsync(Arg.Any<Bike>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task UpdateDampingSpeedCutoffAsync_ReturnsFailed_WhenStoreCommitFails()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        bikeStore.Get(existing.Id).Returns(existing);
        bikeStore.CommitBikeAsync(Arg.Any<Bike>(), Arg.Any<long?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoreMutationResult<BikeSnapshot>>(
                new StoreMutationResult<BikeSnapshot>.Failed("disk full")));

        var result = await CreateCoordinator().UpdateDampingSpeedCutoffAsync(
            existing.Id,
            baselineUpdated: 5,
            SuspensionType.Front,
            DampingSpeedCircuit.Compression,
            300);

        Assert.IsType<BikeDampingSpeedCutoffUpdateResult.Failed>(result);
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveAsync_ReturnsInvalidRearSuspension_WhenLinkageAnalysisIsUnavailable()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        bikeStore.Get(existing.Id).Returns(existing);
        bikeEditorService.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(new BikeEditorAnalysisResult.Unavailable());
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
        await bikeStore.DidNotReceive().CommitBikeAsync(Arg.Any<Bike>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveAsync_WaitsForLinkageAnalysisBeforePersisting()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        bikeStore.Get(existing.Id).Returns(existing);
        var analysisCompletion = new TaskCompletionSource<BikeEditorAnalysisResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bikeEditorService.LoadAnalysisAsync(Arg.Any<RearSuspensionSpec>(), Arg.Any<CancellationToken>())
            .Returns(_ => analysisCompletion.Task);
        var coordinator = CreateCoordinator(UiLayoutProfile.Compact);
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
        _ = bikeStore.DidNotReceive().CommitBikeAsync(Arg.Any<Bike>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        shell.DidNotReceive().GoBack();

        var analysisResult = new BikeEditorAnalysisResult.Computed(
            new BikeAnalysisPresentationData(
                new CoordinateList([0], [0]),
                null));
        analysisCompletion.SetResult(analysisResult);
        var result = await saveTask;

        var saved = Assert.IsType<BikeSaveResult.Saved>(result);
        Assert.Same(analysisResult, saved.AnalysisResult);
        await bikeStore.Received(1).CommitBikeAsync(
            Arg.Is<Bike>(committed => committed.Id == existing.Id),
            5,
            Arg.Any<CancellationToken>());
        shell.Received(1).GoBack();
    }

    [Fact]
    public async Task SaveAsync_ReturnsInvalidRearSuspension_WhenLeverageRatioShockStrokeDoesNotMatchCurveMax()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        bikeStore.Get(existing.Id).Returns(existing);
        var coordinator = CreateCoordinator();

        var bike = Bike.FromSnapshot(TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)),
            shockStroke: 15,
            id: existing.Id,
            updated: 7));

        var result = await coordinator.SaveAsync(bike, baselineUpdated: 5);

        Assert.IsType<BikeSaveResult.InvalidRearSuspension>(result);
        await bikeEditorService.DidNotReceiveWithAnyArgs().LoadAnalysisAsync(default!, default);
        await bikeStore.DidNotReceive().CommitBikeAsync(Arg.Any<Bike>(), Arg.Any<long?>(), Arg.Any<CancellationToken>());
        shell.DidNotReceive().GoBack();
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_ReturnsInUse_WhenDependencyQueryReportsInUse()
    {
        var id = Guid.NewGuid();
        dependencyQuery.IsBikeInUseAsync(id).Returns(true);
        var coordinator = CreateCoordinator();

        var result = await coordinator.DeleteAsync(id);

        Assert.Equal(BikeDeleteOutcome.InUse, result.Outcome);
        await editorFactory.DidNotReceive().CloseBikeEditor(Arg.Any<Guid>());
        await bikeStore.DidNotReceive().CommitBikeDeleteAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeleteAsync_HappyPath_DeletesClosesAndRemovesFromStore()
    {
        var id = Guid.NewGuid();
        dependencyQuery.IsBikeInUseAsync(id).Returns(false);
        var coordinator = CreateCoordinator();

        var result = await coordinator.DeleteAsync(id);

        Assert.Equal(BikeDeleteOutcome.Deleted, result.Outcome);
        await bikeStore.Received(1).CommitBikeDeleteAsync(id, Arg.Any<CancellationToken>());
        await editorFactory.Received(1).CloseBikeEditor(id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailed_WhenDatabaseDeleteThrows_AndDoesNotMutateStoreOrCloseTab()
    {
        var id = Guid.NewGuid();
        dependencyQuery.IsBikeInUseAsync(id).Returns(false);
        bikeStore.CommitBikeDeleteAsync(id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoreDeleteResult<BikeSnapshot>>(
                new StoreDeleteResult<BikeSnapshot>.Failed("locked")));
        var coordinator = CreateCoordinator();

        var result = await coordinator.DeleteAsync(id);

        Assert.Equal(BikeDeleteOutcome.Failed, result.Outcome);
        await editorFactory.DidNotReceive().CloseBikeEditor(Arg.Any<Guid>());
    }

    private static IAppEnvironment CreateEnvironment(UiLayoutProfile layoutProfile) =>
        new AppEnvironment(
            DefaultLayoutProfile: layoutProfile,
            LayoutProfile: layoutProfile,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: true,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: true,
                HasKeyboard: true,
                SupportsLongPressContextMenu: true));
}

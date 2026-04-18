using Avalonia.Headless.XUnit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.BikeEditing;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Coordinators;

public class BikeCoordinatorTests
{
    private readonly IBikeStoreWriter bikeStore = Substitute.For<IBikeStoreWriter>();
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();
    private readonly IBikeDependencyQuery dependencyQuery = Substitute.For<IBikeDependencyQuery>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IBikeEditorService bikeEditorService = Substitute.For<IBikeEditorService>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    private BikeCoordinator CreateCoordinator() => new(
        bikeStore, database, dependencyQuery, shell, bikeEditorService, dialogService);

    // ----- OpenCreateAsync -----

    // [AvaloniaFact] because BikeEditorViewModel construction calls
    // AddInitialJoints → JointViewModel..cctor, which builds Avalonia
    // SolidColorBrush instances. Those have dispatcher-thread affinity,
    // so the test body has to run on the headless UI thread.
    [AvaloniaFact]
    public async Task OpenCreateAsync_OpensNewEditor_OnShell()
    {
        ViewModelBase? captured = null;
        shell.When(s => s.Open(Arg.Any<ViewModelBase>()))
            .Do(c => captured = c.Arg<ViewModelBase>());

        var coordinator = CreateCoordinator();
        await coordinator.OpenCreateAsync();

        shell.Received(1).Open(Arg.Any<ViewModelBase>());
        var editor = Assert.IsType<BikeEditorViewModel>(captured);
        Assert.False(editor.IsInDatabase);
        Assert.True(editor.IsDirty);
    }

    // ----- OpenEditAsync -----

    [Fact]
    public async Task OpenEditAsync_NoOp_WhenSnapshotMissing()
    {
        bikeStore.Get(Arg.Any<Guid>()).Returns((BikeSnapshot?)null);
        var coordinator = CreateCoordinator();

        await coordinator.OpenEditAsync(Guid.NewGuid());

        shell.DidNotReceiveWithAnyArgs().OpenOrFocus<BikeEditorViewModel>(default!, default!);
        shell.DidNotReceiveWithAnyArgs().Open(default!);
    }

    [Fact]
    public async Task OpenEditAsync_RoutesThroughOpenOrFocus_WithIdMatcher()
    {
        var snapshot = TestSnapshots.Bike();
        bikeStore.Get(snapshot.Id).Returns(snapshot);
        var coordinator = CreateCoordinator();

        await coordinator.OpenEditAsync(snapshot.Id);

        shell.Received(1).OpenOrFocus(
            Arg.Any<Func<BikeEditorViewModel, bool>>(),
            Arg.Any<Func<BikeEditorViewModel>>());
    }

    [Fact]
    public async Task LoadAnalysisAsync_DelegatesToBikeEditorService()
    {
        RearSuspension? rearSuspension = new LinkageRearSuspension(new Sufni.Kinematics.Linkage());
        var expected = new BikeEditorAnalysisResult.Unavailable();
        bikeEditorService.LoadAnalysisAsync(rearSuspension, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateCoordinator().LoadAnalysisAsync(rearSuspension);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task LoadImageAsync_DelegatesToBikeEditorService()
    {
        var expected = new BikeImageLoadResult.Canceled();
        bikeEditorService.LoadImageAsync(Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateCoordinator().LoadImageAsync();

        Assert.Same(expected, result);
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
        bikeEditorService.LoadAnalysisAsync(null, Arg.Any<CancellationToken>())
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
    public async Task ImportBikeAsync_SkipsAnalysis_ForInvalidRearSuspensionShape()
    {
        var importedBike = new Bike(Guid.NewGuid(), "mismatched import")
        {
            HeadAngle = 64,
            ForkStroke = 150,
            RearSuspensionKind = RearSuspensionKind.Linkage,
            LeverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)),
        };
        bikeEditorService.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(new BikeFileImportResult.Imported(importedBike));

        var result = await CreateCoordinator().ImportBikeAsync();

        var imported = Assert.IsType<BikeImportResult.Imported>(result);
        Assert.IsType<BikeEditorAnalysisResult.Unavailable>(imported.Data.AnalysisResult);
        await bikeEditorService.DidNotReceiveWithAnyArgs()
            .LoadAnalysisAsync(Arg.Any<RearSuspension?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExportBikeAsync_DelegatesToBikeEditorService()
    {
        var bike = new Bike(Guid.NewGuid(), "export me") { HeadAngle = 65, ForkStroke = 160 };
        var expected = new BikeExportResult.Exported();
        bikeEditorService.ExportBikeAsync(bike, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await CreateCoordinator().ExportBikeAsync(bike);

        Assert.Same(expected, result);
    }

    // ----- SaveAsync -----

    [Fact]
    public async Task SaveAsync_HappyPath_PersistsAndUpserts_AndReturnsSaved()
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

        await database.Received(1).PutAsync(bike);
        bikeStore.Received(1).Upsert(Arg.Is<BikeSnapshot>(s =>
            s.Id == existing.Id &&
            s.Name == "renamed" &&
            s.FrontWheelDiameterMm == 760 &&
            s.RearWheelDiameterMm == 750 &&
            s.ImageRotationDegrees == 13.5 &&
            s.Updated == 7));
        shell.Received(1).GoBack();
        var saved = Assert.IsType<BikeSaveResult.Saved>(result);
        Assert.Equal(7, saved.NewBaselineUpdated);
        Assert.IsType<BikeEditorAnalysisResult.Unavailable>(saved.AnalysisResult);
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
        await database.DidNotReceive().PutAsync(Arg.Any<Bike>());
        bikeStore.DidNotReceive().Upsert(Arg.Any<BikeSnapshot>());
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenDatabasePutThrows_AndDoesNotMutateStore()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        bikeStore.Get(existing.Id).Returns(existing);
        database.PutAsync(Arg.Any<Bike>()).ThrowsAsync(new InvalidOperationException("disk full"));
        var coordinator = CreateCoordinator();

        var bike = new Bike(existing.Id, "x") { HeadAngle = 65, ForkStroke = 160 };

        var result = await coordinator.SaveAsync(bike, baselineUpdated: 5);

        Assert.IsType<BikeSaveResult.Failed>(result);
        bikeStore.DidNotReceive().Upsert(Arg.Any<BikeSnapshot>());
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveAsync_ReturnsInvalidRearSuspension_WhenLinkageAnalysisIsUnavailable()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        bikeStore.Get(existing.Id).Returns(existing);
        bikeEditorService.LoadAnalysisAsync(Arg.Any<RearSuspension?>(), Arg.Any<CancellationToken>())
            .Returns(new BikeEditorAnalysisResult.Unavailable());
        var coordinator = CreateCoordinator();

        var bike = new Bike(existing.Id, "full sus")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            ShockStroke = 50,
            RearSuspensionKind = RearSuspensionKind.Linkage,
            Chainstay = 440,
            PixelsToMillimeters = 1,
            ImageBytes = TestImages.SmallPngBytes(),
            Linkage = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true),
        };

        var result = await coordinator.SaveAsync(bike, baselineUpdated: 5);

        var invalid = Assert.IsType<BikeSaveResult.InvalidRearSuspension>(result);
        Assert.False(string.IsNullOrWhiteSpace(invalid.ErrorMessage));
        await database.DidNotReceive().PutAsync(Arg.Any<Bike>());
        bikeStore.DidNotReceive().Upsert(Arg.Any<BikeSnapshot>());
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
        await database.DidNotReceive().DeleteAsync<Bike>(Arg.Any<Guid>());
        shell.DidNotReceiveWithAnyArgs().CloseIfOpen<BikeEditorViewModel>(default!);
        bikeStore.DidNotReceiveWithAnyArgs().Remove(default);
    }

    [Fact]
    public async Task DeleteAsync_HappyPath_DeletesClosesAndRemovesFromStore()
    {
        var id = Guid.NewGuid();
        dependencyQuery.IsBikeInUseAsync(id).Returns(false);
        var coordinator = CreateCoordinator();

        var result = await coordinator.DeleteAsync(id);

        Assert.Equal(BikeDeleteOutcome.Deleted, result.Outcome);
        await database.Received(1).DeleteAsync<Bike>(id);
        shell.Received(1).CloseIfOpen(Arg.Any<Func<BikeEditorViewModel, bool>>());
        bikeStore.Received(1).Remove(id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailed_WhenDatabaseDeleteThrows_AndDoesNotMutateStoreOrCloseTab()
    {
        var id = Guid.NewGuid();
        dependencyQuery.IsBikeInUseAsync(id).Returns(false);
        database.DeleteAsync<Bike>(id).ThrowsAsync(new InvalidOperationException("locked"));
        var coordinator = CreateCoordinator();

        var result = await coordinator.DeleteAsync(id);

        Assert.Equal(BikeDeleteOutcome.Failed, result.Outcome);
        bikeStore.DidNotReceiveWithAnyArgs().Remove(default);
        shell.DidNotReceiveWithAnyArgs().CloseIfOpen<BikeEditorViewModel>(default!);
    }
}

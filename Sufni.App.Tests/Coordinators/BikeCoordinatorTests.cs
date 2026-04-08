using Avalonia.Headless.XUnit;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Coordinators;

public class BikeCoordinatorTests
{
    private readonly IBikeStoreWriter bikeStore = Substitute.For<IBikeStoreWriter>();
    private readonly ISetupStore setupStore = Substitute.For<ISetupStore>();
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();
    private readonly IBikeDependencyQuery dependencyQuery = Substitute.For<IBikeDependencyQuery>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    private BikeCoordinator CreateCoordinator() => new(
        bikeStore, setupStore, database, dependencyQuery, shell, filesService, dialogService);

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

    // ----- SaveAsync -----

    [Fact]
    public async Task SaveAsync_HappyPath_PersistsAndUpserts_AndReturnsSaved()
    {
        var existing = TestSnapshots.Bike(updated: 5);
        bikeStore.Get(existing.Id).Returns(existing);
        var coordinator = CreateCoordinator();

        var bike = new Bike(existing.Id, "renamed") { HeadAngle = 65, ForkStroke = 160, Updated = 7 };

        var result = await coordinator.SaveAsync(bike, baselineUpdated: 5);

        await database.Received(1).PutAsync(bike);
        bikeStore.Received(1).Upsert(Arg.Is<BikeSnapshot>(s =>
            s.Id == existing.Id && s.Name == "renamed" && s.Updated == 7));
        var saved = Assert.IsType<BikeSaveResult.Saved>(result);
        Assert.Equal(7, saved.NewBaselineUpdated);
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

        var failed = Assert.IsType<BikeSaveResult.Failed>(result);
        Assert.Equal("disk full", failed.ErrorMessage);
        bikeStore.DidNotReceive().Upsert(Arg.Any<BikeSnapshot>());
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
        Assert.Equal("locked", result.ErrorMessage);
        bikeStore.DidNotReceiveWithAnyArgs().Remove(default);
        shell.DidNotReceiveWithAnyArgs().CloseIfOpen<BikeEditorViewModel>(default!);
    }
}

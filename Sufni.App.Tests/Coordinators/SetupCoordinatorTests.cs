using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.Coordinators;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Coordinators;

public class SetupCoordinatorTests
{
    private readonly ISetupStoreWriter setupStore = Substitute.For<ISetupStoreWriter>();
    private readonly IBikeStore bikeStore = Substitute.For<IBikeStore>();
    private readonly IBikeCoordinator bikeCoordinator = Substitute.For<IBikeCoordinator>();
    private readonly IDatabaseService database = Substitute.For<IDatabaseService>();
    private readonly ITelemetryDataStoreService telemetry = Substitute.For<ITelemetryDataStoreService>();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();

    private SetupCoordinator CreateCoordinator() => new(
        setupStore, bikeStore, bikeCoordinator, database, telemetry, shell, dialogService);

    // ----- OpenCreateAsync -----

    [Fact]
    public async Task OpenCreateAsync_OpensNewEditor_WithIsDirtyTrue()
    {
        ViewModelBase? captured = null;
        shell.When(s => s.Open(Arg.Any<ViewModelBase>()))
            .Do(c => captured = c.Arg<ViewModelBase>());

        await CreateCoordinator().OpenCreateAsync();

        shell.Received(1).Open(Arg.Any<ViewModelBase>());
        var editor = Assert.IsType<SetupEditorViewModel>(captured);
        Assert.False(editor.IsInDatabase);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public async Task OpenCreateAsync_HonoursSuggestedBoardId_WhenFree()
    {
        var boardId = Guid.NewGuid();
        setupStore.FindByBoardId(boardId).Returns((SetupSnapshot?)null);

        SetupEditorViewModel? captured = null;
        shell.When(s => s.Open(Arg.Any<ViewModelBase>()))
            .Do(c => captured = c.Arg<ViewModelBase>() as SetupEditorViewModel);

        await CreateCoordinator().OpenCreateAsync(boardId);

        Assert.NotNull(captured);
        Assert.Equal(boardId, captured!.BoardId);
    }

    [Fact]
    public async Task OpenCreateAsync_DropsSuggestedBoardId_WhenAlreadyClaimed()
    {
        var boardId = Guid.NewGuid();
        var existing = TestSnapshots.Setup(boardId: boardId);
        setupStore.FindByBoardId(boardId).Returns(existing);

        SetupEditorViewModel? captured = null;
        shell.When(s => s.Open(Arg.Any<ViewModelBase>()))
            .Do(c => captured = c.Arg<ViewModelBase>() as SetupEditorViewModel);

        await CreateCoordinator().OpenCreateAsync(boardId);

        Assert.NotNull(captured);
        Assert.Null(captured!.BoardId);
    }

    [Fact]
    public async Task OpenCreateForDetectedBoardAsync_ForwardsDetectedBoardId()
    {
        var boardId = Guid.NewGuid();
        telemetry.DetectConnectedBoardIdAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Guid?>(boardId));
        setupStore.FindByBoardId(boardId).Returns((SetupSnapshot?)null);

        SetupEditorViewModel? captured = null;
        shell.When(s => s.Open(Arg.Any<ViewModelBase>()))
            .Do(c => captured = c.Arg<ViewModelBase>() as SetupEditorViewModel);

        await CreateCoordinator().OpenCreateForDetectedBoardAsync();

        await telemetry.Received(1).DetectConnectedBoardIdAsync(Arg.Any<CancellationToken>());
        Assert.NotNull(captured);
        Assert.Equal(boardId, captured!.BoardId);
    }

    // ----- OpenEditAsync -----

    [Fact]
    public async Task OpenEditAsync_NoOp_WhenSnapshotMissing()
    {
        setupStore.Get(Arg.Any<Guid>()).Returns((SetupSnapshot?)null);

        await CreateCoordinator().OpenEditAsync(Guid.NewGuid());

        shell.DidNotReceiveWithAnyArgs().OpenOrFocus<SetupEditorViewModel>(default!, default!);
    }

    [Fact]
    public async Task OpenEditAsync_RoutesThroughOpenOrFocus_WhenSnapshotPresent()
    {
        var snapshot = TestSnapshots.Setup();
        setupStore.Get(snapshot.Id).Returns(snapshot);

        await CreateCoordinator().OpenEditAsync(snapshot.Id);

        shell.Received(1).OpenOrFocus(
            Arg.Any<Func<SetupEditorViewModel, bool>>(),
            Arg.Any<Func<SetupEditorViewModel>>());
    }

    // ----- SaveAsync -----

    [Fact]
    public async Task SaveAsync_HappyPath_PersistsSetup_AndUpsertsSnapshot()
    {
        var existing = TestSnapshots.Setup(updated: 5);
        setupStore.Get(existing.Id).Returns(existing);

        var setup = new Setup(existing.Id, "renamed") { BikeId = existing.BikeId, Updated = 7 };

        var result = await CreateCoordinator().SaveAsync(setup, boardId: existing.BoardId, baselineUpdated: 5);

        await database.Received(1).PutAsync(setup);
        setupStore.Received(1).Upsert(Arg.Is<SetupSnapshot>(s =>
            s.Id == existing.Id && s.Name == "renamed" && s.Updated == 7));
        shell.Received(1).GoBack();
        var saved = Assert.IsType<SetupSaveResult.Saved>(result);
        Assert.Equal(7, saved.NewBaselineUpdated);
    }

    [Fact]
    public async Task SaveAsync_DoesNotTouchBoards_WhenBoardIdUnchanged()
    {
        var boardId = Guid.NewGuid();
        var existing = TestSnapshots.Setup(boardId: boardId, updated: 5);
        setupStore.Get(existing.Id).Returns(existing);

        var setup = new Setup(existing.Id, existing.Name) { BikeId = existing.BikeId, Updated = 6 };

        await CreateCoordinator().SaveAsync(setup, boardId, baselineUpdated: 5);

        await database.DidNotReceive().PutAsync(Arg.Any<Board>());
    }

    [Fact]
    public async Task SaveAsync_ClearsPreviousBoard_AndClaimsNewBoard_WhenChanged()
    {
        var oldBoardId = Guid.NewGuid();
        var newBoardId = Guid.NewGuid();
        var existing = TestSnapshots.Setup(boardId: oldBoardId, updated: 5);
        setupStore.Get(existing.Id).Returns(existing);

        var setup = new Setup(existing.Id, existing.Name) { BikeId = existing.BikeId, Updated = 6 };

        await CreateCoordinator().SaveAsync(setup, newBoardId, baselineUpdated: 5);

        await database.Received(1).PutAsync(Arg.Is<Board>(b =>
            b.Id == oldBoardId && b.SetupId == null));
        await database.Received(1).PutAsync(Arg.Is<Board>(b =>
            b.Id == newBoardId && b.SetupId == existing.Id));
    }

    [Fact]
    public async Task SaveAsync_ClaimsNewBoard_WhenSetupHadNoBoardBefore()
    {
        var newBoardId = Guid.NewGuid();
        var existing = TestSnapshots.Setup(boardId: null, updated: 5);
        setupStore.Get(existing.Id).Returns(existing);

        var setup = new Setup(existing.Id, existing.Name) { BikeId = existing.BikeId, Updated = 6 };

        await CreateCoordinator().SaveAsync(setup, newBoardId, baselineUpdated: 5);

        await database.Received(1).PutAsync(Arg.Is<Board>(b =>
            b.Id == newBoardId && b.SetupId == existing.Id));
        // Only the new-board write — no clear-previous write.
        await database.Received(1).PutAsync(Arg.Any<Board>());
    }

    [Fact]
    public async Task SaveAsync_ClearsPreviousBoard_WhenBoardCleared()
    {
        var oldBoardId = Guid.NewGuid();
        var existing = TestSnapshots.Setup(boardId: oldBoardId, updated: 5);
        setupStore.Get(existing.Id).Returns(existing);

        var setup = new Setup(existing.Id, existing.Name) { BikeId = existing.BikeId, Updated = 6 };

        await CreateCoordinator().SaveAsync(setup, boardId: null, baselineUpdated: 5);

        await database.Received(1).PutAsync(Arg.Is<Board>(b =>
            b.Id == oldBoardId && b.SetupId == null));
        await database.Received(1).PutAsync(Arg.Any<Board>());
    }

    [Fact]
    public async Task SaveAsync_ReturnsConflict_WhenStoreIsNewer()
    {
        var current = TestSnapshots.Setup(updated: 10);
        setupStore.Get(current.Id).Returns(current);

        var setup = new Setup(current.Id, "stale") { BikeId = current.BikeId };

        var result = await CreateCoordinator().SaveAsync(setup, boardId: null, baselineUpdated: 5);

        var conflict = Assert.IsType<SetupSaveResult.Conflict>(result);
        Assert.Same(current, conflict.CurrentSnapshot);
        await database.DidNotReceive().PutAsync(Arg.Any<Setup>());
        setupStore.DidNotReceive().Upsert(Arg.Any<SetupSnapshot>());
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenDatabasePutThrows()
    {
        var existing = TestSnapshots.Setup(updated: 5);
        setupStore.Get(existing.Id).Returns(existing);
        database.PutAsync(Arg.Any<Setup>()).ThrowsAsync(new InvalidOperationException("disk full"));

        var setup = new Setup(existing.Id, existing.Name) { BikeId = existing.BikeId };

        var result = await CreateCoordinator().SaveAsync(setup, boardId: null, baselineUpdated: 5);

        Assert.IsType<SetupSaveResult.Failed>(result);
        setupStore.DidNotReceive().Upsert(Arg.Any<SetupSnapshot>());
        shell.DidNotReceive().GoBack();
    }

    // ----- DeleteAsync -----

    [Fact]
    public async Task DeleteAsync_HappyPath_DeletesClosesAndRemoves()
    {
        var snapshot = TestSnapshots.Setup();
        setupStore.Get(snapshot.Id).Returns(snapshot);

        var result = await CreateCoordinator().DeleteAsync(snapshot.Id);

        Assert.Equal(SetupDeleteOutcome.Deleted, result.Outcome);
        await database.Received(1).DeleteAsync<Setup>(snapshot.Id);
        shell.Received(1).CloseIfOpen(Arg.Any<Func<SetupEditorViewModel, bool>>());
        setupStore.Received(1).Remove(snapshot.Id);
    }

    [Fact]
    public async Task DeleteAsync_ClearsPreviousBoard_BeforeRemovingFromStore()
    {
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(boardId: boardId);
        setupStore.Get(snapshot.Id).Returns(snapshot);

        var result = await CreateCoordinator().DeleteAsync(snapshot.Id);

        Assert.Equal(SetupDeleteOutcome.Deleted, result.Outcome);
        await database.Received(1).PutAsync(Arg.Is<Board>(b =>
            b.Id == boardId && b.SetupId == null));
    }

    [Fact]
    public async Task DeleteAsync_StillReportsDeleted_WhenBoardClearThrows()
    {
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(boardId: boardId);
        setupStore.Get(snapshot.Id).Returns(snapshot);
        database.PutAsync(Arg.Any<Board>()).ThrowsAsync(new InvalidOperationException("board write blew up"));

        var result = await CreateCoordinator().DeleteAsync(snapshot.Id);

        Assert.Equal(SetupDeleteOutcome.Deleted, result.Outcome);
        setupStore.Received(1).Remove(snapshot.Id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailed_WhenSetupDeleteThrows()
    {
        var snapshot = TestSnapshots.Setup();
        setupStore.Get(snapshot.Id).Returns(snapshot);
        database.DeleteAsync<Setup>(snapshot.Id).ThrowsAsync(new InvalidOperationException("locked"));

        var result = await CreateCoordinator().DeleteAsync(snapshot.Id);

        Assert.Equal(SetupDeleteOutcome.Failed, result.Outcome);
        setupStore.DidNotReceiveWithAnyArgs().Remove(default);
        shell.DidNotReceiveWithAnyArgs().CloseIfOpen<SetupEditorViewModel>(default!);
    }
}

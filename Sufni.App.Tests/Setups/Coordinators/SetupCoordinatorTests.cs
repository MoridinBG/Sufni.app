using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Acquisition.Services;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Shell.Coordinators;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Setups.Coordinators;

public class SetupCoordinatorTests
{
    private readonly ISetupStoreWriter setupStore = Substitute.For<ISetupStoreWriter>();
    private readonly IBikeStoreWriter bikeStore = Substitute.For<IBikeStoreWriter>();
    private readonly IBikeCoordinator bikeCoordinator = TestCoordinatorSubstitutes.Bike();
    private readonly ISynchronizableRepository<Setup> setupRepository = Substitute.For<ISynchronizableRepository<Setup>>();
    private readonly ISynchronizableRepository<Bike> bikeRepository = Substitute.For<ISynchronizableRepository<Bike>>();
    private readonly ISynchronizableRepository<Board> boardRepository = Substitute.For<ISynchronizableRepository<Board>>();
    private readonly ITelemetryDataStoreService telemetry = Substitute.For<ITelemetryDataStoreService>();
    private readonly IFilesService filesService = Substitute.For<IFilesService>();
    private readonly IBackgroundTaskRunner backgroundTaskRunner = new InlineBackgroundTaskRunner();
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private readonly IDialogService dialogService = Substitute.For<IDialogService>();
    private readonly IUiThreadDispatcher uiThreadDispatcher = new InlineUiThreadDispatcher();
    private readonly IEditorFactory editorFactory = Substitute.For<IEditorFactory>();

    public SetupCoordinatorTests()
    {
        editorFactory.CloseSetupEditor(Arg.Any<Guid>()).Returns(Task.CompletedTask);
    }

    private SetupCoordinator CreateCoordinator(UiLayoutProfile layoutProfile = UiLayoutProfile.Workspace)
    {
        SetupCoordinator? coordinator = null;
        coordinator = new(
            setupStore,
            bikeStore,
            setupRepository,
            bikeRepository,
            boardRepository,
            telemetry,
            filesService,
            backgroundTaskRunner,
            shell,
            CreateEnvironment(layoutProfile),
            () => editorFactory);
        return coordinator;
    }

    // ----- OpenCreateAsync -----

    [Fact]
    public async Task OpenCreateAsync_OpensNewEditor_ThroughFactory()
    {
        await CreateCoordinator().OpenCreateAsync();

        editorFactory.Received(1).OpenNewSetupEditor(Arg.Is<SetupSnapshot>(snapshot =>
            snapshot.Name == "new setup"));
    }

    [Fact]
    public async Task OpenCreateAsync_HonoursSuggestedBoardId_WhenFree()
    {
        var boardId = Guid.NewGuid();
        setupStore.FindByBoardId(boardId).Returns((SetupSnapshot?)null);

        SetupSnapshot? captured = null;
        editorFactory.When(factory => factory.OpenNewSetupEditor(Arg.Any<SetupSnapshot>()))
            .Do(call => captured = call.Arg<SetupSnapshot>());

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

        SetupSnapshot? captured = null;
        editorFactory.When(factory => factory.OpenNewSetupEditor(Arg.Any<SetupSnapshot>()))
            .Do(call => captured = call.Arg<SetupSnapshot>());

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

        SetupSnapshot? captured = null;
        editorFactory.When(factory => factory.OpenNewSetupEditor(Arg.Any<SetupSnapshot>()))
            .Do(call => captured = call.Arg<SetupSnapshot>());

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

        editorFactory.DidNotReceive().OpenSetupEditor(Arg.Any<SetupSnapshot>());
    }

    [Fact]
    public async Task OpenEditAsync_OpensEditor_ThroughFactory()
    {
        var snapshot = TestSnapshots.Setup();
        setupStore.Get(snapshot.Id).Returns(snapshot);

        await CreateCoordinator().OpenEditAsync(snapshot.Id);

        editorFactory.Received(1).OpenSetupEditor(snapshot);
    }

    // ----- SaveAsync -----

    [Fact]
    public async Task SaveAsync_HappyPath_PersistsSetup_AndUpsertsSnapshot()
    {
        var existing = TestSnapshots.Setup(updated: 5);
        setupStore.Get(existing.Id).Returns(existing);

        var setup = new Setup(existing.Id, "renamed") { BikeId = existing.BikeId, Updated = 7 };

        var result = await CreateCoordinator().SaveAsync(setup, boardId: existing.BoardId, baselineUpdated: 5);

        await setupRepository.Received(1).PutAsync(setup);
        setupStore.Received(1).Upsert(Arg.Is<SetupSnapshot>(s =>
            s.Id == existing.Id && s.Name == "renamed" && s.Updated == 7));
        shell.DidNotReceive().GoBack();
        var saved = Assert.IsType<SetupSaveResult.Saved>(result);
        Assert.Equal(7, saved.NewBaselineUpdated);
    }

    [Fact]
    public async Task SaveAsync_OnCompact_NavigatesBackAfterSave()
    {
        var existing = TestSnapshots.Setup(updated: 5);
        setupStore.Get(existing.Id).Returns(existing);
        var setup = new Setup(existing.Id, "renamed") { BikeId = existing.BikeId, Updated = 7 };

        await CreateCoordinator(UiLayoutProfile.Compact).SaveAsync(
            setup,
            boardId: existing.BoardId,
            baselineUpdated: 5);

        shell.Received(1).GoBack();
    }

    [Fact]
    public async Task SaveAsync_DoesNotTouchBoards_WhenBoardIdUnchanged()
    {
        var boardId = Guid.NewGuid();
        var existing = TestSnapshots.Setup(boardId: boardId, updated: 5);
        setupStore.Get(existing.Id).Returns(existing);

        var setup = new Setup(existing.Id, existing.Name) { BikeId = existing.BikeId, Updated = 6 };

        await CreateCoordinator().SaveAsync(setup, boardId, baselineUpdated: 5);

        await boardRepository.DidNotReceive().PutAsync(Arg.Any<Board>());
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

        await boardRepository.Received(1).PutAsync(Arg.Is<Board>(b =>
            b.Id == oldBoardId && b.SetupId == null));
        await boardRepository.Received(1).PutAsync(Arg.Is<Board>(b =>
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

        await boardRepository.Received(1).PutAsync(Arg.Is<Board>(b =>
            b.Id == newBoardId && b.SetupId == existing.Id));
        // Only the new-board write — no clear-previous write.
        await boardRepository.Received(1).PutAsync(Arg.Any<Board>());
    }

    [Fact]
    public async Task SaveAsync_ClearsPreviousBoard_WhenBoardCleared()
    {
        var oldBoardId = Guid.NewGuid();
        var existing = TestSnapshots.Setup(boardId: oldBoardId, updated: 5);
        setupStore.Get(existing.Id).Returns(existing);

        var setup = new Setup(existing.Id, existing.Name) { BikeId = existing.BikeId, Updated = 6 };

        await CreateCoordinator().SaveAsync(setup, boardId: null, baselineUpdated: 5);

        await boardRepository.Received(1).PutAsync(Arg.Is<Board>(b =>
            b.Id == oldBoardId && b.SetupId == null));
        await boardRepository.Received(1).PutAsync(Arg.Any<Board>());
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
        await setupRepository.DidNotReceive().PutAsync(Arg.Any<Setup>());
        setupStore.DidNotReceive().Upsert(Arg.Any<SetupSnapshot>());
        shell.DidNotReceive().GoBack();
    }

    [Fact]
    public async Task SaveAsync_ReturnsFailed_WhenDatabasePutThrows()
    {
        var existing = TestSnapshots.Setup(updated: 5);
        setupStore.Get(existing.Id).Returns(existing);
        setupRepository.PutAsync(Arg.Any<Setup>()).ThrowsAsync(new InvalidOperationException("disk full"));

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
        await setupRepository.Received(1).DeleteAsync(snapshot.Id);
        await editorFactory.Received(1).CloseSetupEditor(snapshot.Id);
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
        await boardRepository.Received(1).PutAsync(Arg.Is<Board>(b =>
            b.Id == boardId && b.SetupId == null));
    }

    [Fact]
    public async Task DeleteAsync_StillReportsDeleted_WhenBoardClearThrows()
    {
        var boardId = Guid.NewGuid();
        var snapshot = TestSnapshots.Setup(boardId: boardId);
        setupStore.Get(snapshot.Id).Returns(snapshot);
        boardRepository.PutAsync(Arg.Any<Board>()).ThrowsAsync(new InvalidOperationException("board write blew up"));

        var result = await CreateCoordinator().DeleteAsync(snapshot.Id);

        Assert.Equal(SetupDeleteOutcome.Deleted, result.Outcome);
        setupStore.Received(1).Remove(snapshot.Id);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFailed_WhenSetupDeleteThrows()
    {
        var snapshot = TestSnapshots.Setup();
        setupStore.Get(snapshot.Id).Returns(snapshot);
        setupRepository.DeleteAsync(snapshot.Id).ThrowsAsync(new InvalidOperationException("locked"));

        var result = await CreateCoordinator().DeleteAsync(snapshot.Id);

        Assert.Equal(SetupDeleteOutcome.Failed, result.Outcome);
        setupStore.DidNotReceiveWithAnyArgs().Remove(default);
        await editorFactory.DidNotReceive().CloseSetupEditor(Arg.Any<Guid>());
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

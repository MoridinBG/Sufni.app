using Sufni.App.Shared.Models;
using Sufni.App.Shell.Coordinators;
using Microsoft.Extensions.Time.Testing;
using DynamicData;
using NSubstitute;

using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Queries;
using Sufni.App.Setups.ViewModels.ItemLists;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Doubles;
namespace Sufni.App.Tests.Setups.ViewModels.ItemLists;

public class SetupListViewModelTests
{
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private static readonly InlineUiThreadDispatcher UiThreadDispatcher = new();

    [Fact]
    public void AddCommand_OpensSetupCreate_WithoutImportPageBoardContext()
    {
        var setupStore = Substitute.For<ISetupBoardQuery>();
        using var setupCache = new SourceCache<SetupDetails, Guid>(snapshot => snapshot.Setup.Id);
        var setupCoordinator = TestCoordinatorSubstitutes.Setup();

        setupStore.Connect().Returns(setupCache.Connect());

        var viewModel = new SetupListViewModel(setupStore, UiThreadDispatcher, pendingDeletes: TestPendingDeletes.Create(setups: setupCoordinator, shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());

        viewModel.AddCommand.Execute(null);

        shell.Received(1).OpenEditor(new EditorKey.NewSetup());
    }

    [Fact]
    public async Task FinalizeDelete_KeepsSetupHidden_WhileDeleteInProgress()
    {
        var setupStore = Substitute.For<ISetupBoardQuery>();
        using var setupCache = new SourceCache<SetupDetails, Guid>(snapshot => snapshot.Setup.Id);
        setupStore.Connect().Returns(setupCache.Connect());

        var snapshot = TestSnapshots.Setup(name: "to delete");
        setupCache.AddOrUpdate(new SetupDetails(snapshot, null));
        setupStore.Get(snapshot.Id).Returns(new SetupDetails(snapshot, null));

        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        var deleteTcs = new TaskCompletionSource<SetupDeleteResult>();
        setupCoordinator.DeleteAsync(snapshot.Id).Returns(deleteTcs.Task);

        var viewModel = new SetupListViewModel(setupStore, UiThreadDispatcher, pendingDeletes: TestPendingDeletes.Create(setups: setupCoordinator, shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        viewModel.LoadedCommand.Execute(null);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        Assert.Empty(viewModel.Items);

        var entry = viewModel.PendingDeletes[0];
        var finalizeTask = entry.FinalizeDeleteCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.Items);

        setupCache.Remove(snapshot.Id);
        deleteTcs.SetResult(new SetupDeleteResult.Deleted());
        await finalizeTask;

        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public void Items_UpdateOnlyWhileLoaded_AndReloadCurrentStoreState()
    {
        var setupStore = Substitute.For<ISetupBoardQuery>();
        using var setupCache = new SourceCache<SetupDetails, Guid>(snapshot => snapshot.Setup.Id);
        setupStore.Connect().Returns(setupCache.Connect());
        var first = TestSnapshots.Setup(name: "first");
        var second = TestSnapshots.Setup(name: "second");
        setupCache.AddOrUpdate(new SetupDetails(first, null));

        var viewModel = new SetupListViewModel(
            setupStore,
            UiThreadDispatcher,
            pendingDeletes: TestPendingDeletes.Create(setups: TestCoordinatorSubstitutes.Setup(), shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());

        Assert.Empty(viewModel.Items);

        viewModel.LoadedCommand.Execute(null);
        Assert.Single(viewModel.Items);

        viewModel.UnloadedCommand.Execute(null);
        setupCache.AddOrUpdate(new SetupDetails(second, null));
        Assert.Empty(viewModel.Items);

        viewModel.LoadedCommand.Execute(null);
        viewModel.LoadedCommand.Execute(null);
        Assert.Equal(2, viewModel.Items.Count);
    }

    [Fact]
    public async Task FinalizeDelete_RestoresSetup_WhenCoordinatorReportsFailure()
    {
        var setupStore = Substitute.For<ISetupBoardQuery>();
        using var setupCache = new SourceCache<SetupDetails, Guid>(snapshot => snapshot.Setup.Id);
        setupStore.Connect().Returns(setupCache.Connect());

        var snapshot = TestSnapshots.Setup(name: "stays");
        setupCache.AddOrUpdate(new SetupDetails(snapshot, null));
        setupStore.Get(snapshot.Id).Returns(new SetupDetails(snapshot, null));

        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        setupCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SetupDeleteResult.Failed(SetupDeleteFailureReason.Delete, "boom"));

        var viewModel = new SetupListViewModel(setupStore, UiThreadDispatcher, pendingDeletes: TestPendingDeletes.Create(setups: setupCoordinator, shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        viewModel.LoadedCommand.Execute(null);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        var entry = viewModel.PendingDeletes[0];
        await entry.FinalizeDeleteCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Items);
        Assert.NotEmpty(viewModel.ErrorMessages);
    }
}

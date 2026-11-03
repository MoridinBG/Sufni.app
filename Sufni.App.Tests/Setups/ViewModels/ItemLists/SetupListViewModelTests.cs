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
    public async Task FinalizeDelete_KeepsSetupHidden_WhileDeleteInProgress()
    {
        using var setupStore = new SetupBoardQueryStub();

        var snapshot = TestSnapshots.Setup(name: "to delete");
        setupStore.Add(snapshot);

        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        var deleteTcs = new TaskCompletionSource<SetupDeleteResult>();
        setupCoordinator.DeleteAsync(snapshot.Id).Returns(deleteTcs.Task);

        using var viewModel = new SetupListViewModel(setupStore, setupCoordinator, UiThreadDispatcher, pendingDeletes: TestPendingDeletes.Create(setups: setupCoordinator, shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        viewModel.LoadedCommand.Execute(null);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        Assert.Empty(viewModel.Items);

        var entry = viewModel.PendingDeletes[0];
        var finalizeTask = entry.FinalizeDeleteCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.Items);

        setupStore.Remove(snapshot.Id);
        deleteTcs.SetResult(new SetupDeleteResult.Deleted());
        await finalizeTask.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(viewModel.Items);
    }

    [Fact]
    public void Items_UpdateOnlyWhileLoaded_AndReloadCurrentStoreState()
    {
        using var setupStore = new SetupBoardQueryStub();
        var first = TestSnapshots.Setup(name: "first");
        var second = TestSnapshots.Setup(name: "second");
        setupStore.Add(first);

        using var viewModel = new SetupListViewModel(
            setupStore,
            TestCoordinatorSubstitutes.Setup(),
            UiThreadDispatcher,
            pendingDeletes: TestPendingDeletes.Create(setups: TestCoordinatorSubstitutes.Setup(), shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());

        Assert.Empty(viewModel.Items);

        viewModel.LoadedCommand.Execute(null);
        Assert.Single(viewModel.Items);

        viewModel.UnloadedCommand.Execute(null);
        setupStore.Add(second);
        Assert.Empty(viewModel.Items);

        viewModel.LoadedCommand.Execute(null);
        viewModel.LoadedCommand.Execute(null);
        Assert.Equal(2, viewModel.Items.Count);
    }

    [Fact]
    public async Task FinalizeDelete_RestoresSetup_WhenCoordinatorReportsFailure()
    {
        using var setupStore = new SetupBoardQueryStub();

        var snapshot = TestSnapshots.Setup(name: "stays");
        setupStore.Add(snapshot);

        var setupCoordinator = TestCoordinatorSubstitutes.Setup();
        setupCoordinator.DeleteAsync(snapshot.Id)
            .Returns(new SetupDeleteResult.Failed(SetupDeleteFailureReason.Delete, "boom"));

        using var viewModel = new SetupListViewModel(setupStore, setupCoordinator, UiThreadDispatcher, pendingDeletes: TestPendingDeletes.Create(setups: setupCoordinator, shell: shell), timeProvider: new FakeTimeProvider(), shell: shell, shellActions: TestShellFeedback.Create());
        viewModel.LoadedCommand.Execute(null);
        Assert.Single(viewModel.Items);

        viewModel.Items[0].UndoableDeleteCommand.Execute(null);
        var entry = viewModel.PendingDeletes[0];
        await entry.FinalizeDeleteCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Items);
        Assert.NotEmpty(viewModel.ErrorMessages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Import_CancelsOnDetachAndIgnoresLateCompletion(bool dispose)
    {
        var coordinator = TestCoordinatorSubstitutes.Setup();
        var pending = new TaskCompletionSource<SetupImportResult>();
        CancellationToken token = default;
        coordinator.ImportSetupAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            token = call.Arg<CancellationToken>();
            return pending.Task;
        });
        using var query = new SetupBoardQueryStub();
        using var viewModel = new SetupListViewModel(query, coordinator,
            UiThreadDispatcher, TestPendingDeletes.Create(setups: coordinator, shell: shell),
            new FakeTimeProvider(), shell, TestShellFeedback.Create());
        viewModel.LoadedCommand.Execute(null);
        var task = viewModel.ImportCommand.ExecuteAsync(null);

        if (dispose) viewModel.Dispose();
        else viewModel.UnloadedCommand.Execute(null);
        Assert.True(token.IsCancellationRequested);
        var setupId = Guid.NewGuid();
        var imported = new SetupImportResult.Imported(setupId);
        pending.SetResult(imported);
        await task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        shell.DidNotReceive().OpenEditor(Arg.Any<EditorKey>());
        Assert.Empty(viewModel.ErrorMessages);
    }

    [Fact]
    public async Task Import_ReportsFailureWithoutNavigation_AndCancellationIsNeutral()
    {
        var coordinator = TestCoordinatorSubstitutes.Setup();
        coordinator.ImportSetupAsync(Arg.Any<CancellationToken>())
            .Returns(new SetupImportResult.InvalidFile("Invalid file"), new SetupImportResult.Canceled());
        using var query = new SetupBoardQueryStub();
        using var viewModel = new SetupListViewModel(query, coordinator,
            UiThreadDispatcher, TestPendingDeletes.Create(setups: coordinator, shell: shell),
            new FakeTimeProvider(), shell, TestShellFeedback.Create());

        await viewModel.ImportCommand.ExecuteAsync(null);
        Assert.Single(viewModel.ErrorMessages);
        viewModel.ErrorMessages.Clear();
        await viewModel.ImportCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.ErrorMessages);
        shell.DidNotReceive().OpenEditor(Arg.Any<EditorKey>());
    }
}

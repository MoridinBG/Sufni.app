using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.ViewModels.ItemLists;

public class ItemListViewModelBaseTests
{
    [Fact]
    public async Task PendingDeleteEntry_AddsError_WhenFinalizeThrows()
    {
        var viewModel = new TestItemListViewModel();
        viewModel.BeginPendingDelete(() => throw new InvalidOperationException("flush failure"));

        var entry = Assert.Single(viewModel.PendingDeletes);
        await entry.FinalizeDeleteCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.ErrorMessages, message => message.Contains("flush failure", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunPendingDeleteInteractionAsync_AddsError_WhenActionThrows()
    {
        var viewModel = new TestItemListViewModel();

        await viewModel.RunPendingDeleteInteractionForTestAsync(() => throw new InvalidOperationException("interaction failure"));

        Assert.Contains(viewModel.ErrorMessages, message => message.Contains("interaction failure", StringComparison.Ordinal));
    }

    private sealed class TestItemListViewModel : ItemListViewModelBase
    {
        public void BeginPendingDelete(Func<Task> finalize)
        {
            StartUndoWindow("test", finalize, () => { });
        }

        public Task RunPendingDeleteInteractionForTestAsync(Func<Task> action)
        {
            return RunActionSwallowExceptionToErrorMessages(action);
        }
    }
}

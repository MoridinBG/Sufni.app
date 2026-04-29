using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class UndoDeleteButtonTests
{
    [AvaloniaFact]
    public async Task UndoDeleteButton_RendersBar_AndDismissButtonFinalizesDelete()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var viewModel = new TestUndoItemListViewModel();
        viewModel.BeginPendingDelete("Morning Ride");

        var view = new UndoDeleteButton
        {
            DataContext = viewModel
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var dismissButton = Assert.Single(view.FindAllVisual<Button>(), b => b.Name == "DismissButton");
            Assert.Equal("Morning Ride", Assert.Single(viewModel.PendingDeletes).Name);

            dismissButton.Command!.Execute(dismissButton.CommandParameter);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(1, viewModel.FinalizeCount);
            Assert.Equal(0, viewModel.UndoCount);
            Assert.Empty(viewModel.PendingDeletes);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task UndoDeleteButton_UndoButton_CancelsDeleteWithoutFinalizing()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var viewModel = new TestUndoItemListViewModel();
        viewModel.BeginPendingDelete("Race Setup");

        var view = new UndoDeleteButton
        {
            DataContext = viewModel
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var undoButton = Assert.Single(view.FindAllVisual<Button>(), b => b.Name == "UndoButton");

            undoButton.Command!.Execute(undoButton.CommandParameter);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(0, viewModel.FinalizeCount);
            Assert.Equal(1, viewModel.UndoCount);
            Assert.Empty(viewModel.PendingDeletes);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task UndoDeleteButton_StacksMultipleEntries()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var viewModel = new TestUndoItemListViewModel();
        viewModel.BeginPendingDelete("First");
        viewModel.BeginPendingDelete("Second");

        var view = new UndoDeleteButton
        {
            DataContext = viewModel
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);

        try
        {
            var dismissButtons = view.FindAllVisual<Button>().Where(b => b.Name == "DismissButton").ToArray();
            Assert.Equal(2, dismissButtons.Length);
            Assert.Equal(2, viewModel.PendingDeletes.Count);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}

internal sealed class TestUndoItemListViewModel : ItemListViewModelBase
{
    public int FinalizeCount { get; private set; }
    public int UndoCount { get; private set; }

    public void BeginPendingDelete(string name)
    {
        StartUndoWindow(
            name,
            () =>
            {
                FinalizeCount++;
                return Task.CompletedTask;
            },
            () => UndoCount++);
    }
}

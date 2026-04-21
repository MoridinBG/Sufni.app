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
    public async Task UndoDeleteButton_ShowsPendingName_AndDismissButtonFinalizesDelete()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var viewModel = new TestUndoItemListViewModel();
        viewModel.BeginPendingDelete("Morning Ride");

        var view = new UndoDeleteButton
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var dismissButton = view.FindControl<Button>("DismissButton");
            Assert.NotNull(dismissButton);
            Assert.True(view.IsVisible);
            Assert.Equal("Morning Ride", viewModel.PendingName);

            dismissButton!.Command!.Execute(dismissButton.CommandParameter);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(1, viewModel.FinalizeCount);
            Assert.Equal(0, viewModel.UndoCount);
            Assert.False(view.IsVisible);
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

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var undoButton = view.FindControl<Button>("UndoButton");
            Assert.NotNull(undoButton);

            undoButton!.Command!.Execute(undoButton.CommandParameter);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(0, viewModel.FinalizeCount);
            Assert.Equal(1, viewModel.UndoCount);
            Assert.False(view.IsVisible);
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
        StartUndoWindow(name, () =>
        {
            FinalizeCount++;
            return Task.CompletedTask;
        });
    }

    protected override void OnPendingDeleteUndone()
    {
        UndoCount++;
    }
}
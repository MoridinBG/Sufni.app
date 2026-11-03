using Sufni.App.Shell.ViewModels.Base;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Labs.Controls;
using Avalonia.VisualTree;

using Sufni.App.Shared.Base;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shared.Views.Controls;

[Collection("Ui")]
public class SwipeToDeleteButtonTests
{

    [AvaloniaFact]
    public async Task SwipeToDeleteButton_SwipeStateLeftVisible_InvokesDeleteCommand()
    {
        var row = new TestListItemRowViewModel();
        row.SetState("Morning Ride", null, isComplete: true);

        var view = new SwipeToDeleteButton
        {
            DataContext = row
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var swipe = view.FindControl<Swipe>("SwipeButton");
            Assert.NotNull(swipe);

            swipe!.SwipeState = SwipeState.LeftVisible;
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(1, row.DeleteCount);
            Assert.Equal(SwipeState.Hidden, swipe.SwipeState);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}

internal sealed class TestListItemRowViewModel : ListItemRowViewModelBase
{
    public int OpenCount { get; private set; }
    public int DeleteCount { get; private set; }

    public void SetState(string name, DateTime? timestamp, bool isComplete)
    {
        Name = name;
        Timestamp = timestamp;
        IsComplete = isComplete;
    }

    protected override Task OpenPageAsync()
    {
        OpenCount++;
        return Task.CompletedTask;
    }

    protected override void UndoableDelete()
    {
        DeleteCount++;
    }
}

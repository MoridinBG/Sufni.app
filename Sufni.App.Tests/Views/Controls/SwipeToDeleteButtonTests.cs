using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Labs.Controls;
using Avalonia.VisualTree;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Rows;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class SwipeToDeleteButtonTests
{
    [AvaloniaFact]
    public async Task SwipeToDeleteButton_BindsRowContent_AndRunsOpenCommand()
    {
        var row = new TestListItemRowViewModel();
        row.SetState("Morning Ride", new DateTime(2025, 1, 2, 8, 30, 0), isComplete: false);

        var view = new SwipeToDeleteButton
        {
            DataContext = row
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var openButton = view.FindControl<Button>("OpenButton");
            var nameText = view.FindControl<TextBlock>("NameTextBlock");
            var incompleteIcon = view.FindControl<Image>("IncompleteStatusIcon");

            Assert.NotNull(openButton);
            Assert.NotNull(nameText);
            Assert.NotNull(incompleteIcon);
            Assert.Equal("Morning Ride", nameText!.Text);
            Assert.True(incompleteIcon!.IsVisible);

            openButton.Command!.Execute(openButton.CommandParameter);
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.Equal(1, row.OpenCount);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

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
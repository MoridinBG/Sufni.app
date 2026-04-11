using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Controls.Primitives;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class CommonButtonLineTests
{
    [AvaloniaFact]
    public async Task CommonButtonLine_SaveButton_BindsConfiguredCommand()
    {
        var saveCommand = new RelayCommand(() => { }, () => false);
        var view = new CommonButtonLine
        {
            DataContext = CreateActions(saveCommand: saveCommand)
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var saveButton = view.FindControl<Button>("SaveButton");
            Assert.NotNull(saveButton);

            Assert.Same(saveCommand, saveButton!.Command);
            Assert.False(saveButton.Command!.CanExecute(saveButton.CommandParameter));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task CommonButtonLine_SaveButton_CommandCanExecuteReflectsCurrentState()
    {
        var saveCommand = new RelayCommand(() => { }, () => true);
        var view = new CommonButtonLine
        {
            DataContext = CreateActions(saveCommand: saveCommand)
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var saveButton = view.FindControl<Button>("SaveButton");
            Assert.NotNull(saveButton);

            Assert.Same(saveCommand, saveButton!.Command);
            Assert.True(saveButton.Command!.CanExecute(saveButton.CommandParameter));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task CommonButtonLine_SaveButton_InvokesSaveCommand()
    {
        var saveInvoked = false;
        var view = new CommonButtonLine
        {
            DataContext = CreateActions(saveCommand: new RelayCommand(() => saveInvoked = true))
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var saveButton = view.FindControl<Button>("SaveButton");
            Assert.NotNull(saveButton);

            saveButton!.Command!.Execute(saveButton.CommandParameter);

            Assert.True(saveInvoked);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task CommonButtonLine_BackButton_InvokesBackCommand()
    {
        var backInvoked = false;
        var view = new CommonButtonLine
        {
            DataContext = CreateActions(openPreviousPageCommand: new RelayCommand(() => backInvoked = true))
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var backButton = view.FindControl<Button>("BackButton");
            Assert.NotNull(backButton);

            backButton!.Command!.Execute(backButton.CommandParameter);

            Assert.True(backInvoked);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task CommonButtonLine_ResetButton_TracksCanExecuteChanges()
    {
        var canReset = false;
        var resetCommand = new RelayCommand(() => { }, () => canReset);
        var view = new CommonButtonLine
        {
            DataContext = CreateActions(resetCommand: resetCommand)
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var resetButton = view.FindControl<Button>("ResetButton");
            Assert.NotNull(resetButton);

            Assert.Same(resetCommand, resetButton!.Command);
            Assert.False(resetButton.Command!.CanExecute(resetButton.CommandParameter));

            canReset = true;
            resetCommand.NotifyCanExecuteChanged();
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.True(resetButton.Command!.CanExecute(resetButton.CommandParameter));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task CommonButtonLine_DeleteFlyout_BindsDeleteConfirmationContent()
    {
        object? deleteParameter = null;
        var fakeDeleteInvoked = false;
        var fakeDeleteCommand = new RelayCommand(() => fakeDeleteInvoked = true);
        var view = new CommonButtonLine
        {
            DataContext = CreateActions(
                deleteCommand: new RelayCommand<object?>(parameter => deleteParameter = parameter),
                fakeDeleteCommand: fakeDeleteCommand)
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var deleteButton = view.FindControl<Button>("DeleteButton");
            Assert.NotNull(deleteButton);
            var flyout = Assert.IsType<Flyout>(deleteButton!.Flyout);
            var flyoutContent = Assert.IsType<StackPanel>(flyout.Content);
            flyoutContent.DataContext = view.DataContext;
            await ViewTestHelpers.FlushDispatcherAsync();

            deleteButton.Command!.Execute(deleteButton.CommandParameter);

            var innerDeleteButton = Assert.Single(flyoutContent.Children.OfType<Button>(), button => Equals(button.Content, "delete"));
            Assert.NotNull(innerDeleteButton.Command);

            innerDeleteButton.Command!.Execute(innerDeleteButton.CommandParameter);

            Assert.Same(fakeDeleteCommand, deleteButton.Command);
            Assert.True(fakeDeleteInvoked);
            Assert.Equal(true, deleteParameter);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    private static IEditorActions CreateActions(
        IRelayCommand? openPreviousPageCommand = null,
        IRelayCommand? saveCommand = null,
        IRelayCommand? resetCommand = null,
        IRelayCommand? deleteCommand = null,
        IRelayCommand? fakeDeleteCommand = null)
    {
        var actions = Substitute.For<IEditorActions>();

        actions.OpenPreviousPageCommand.Returns(openPreviousPageCommand ?? new RelayCommand(() => { }));
        actions.SaveCommand.Returns(saveCommand ?? new RelayCommand(() => { }));
        actions.ResetCommand.Returns(resetCommand ?? new RelayCommand(() => { }));
        actions.DeleteCommand.Returns(deleteCommand ?? new RelayCommand(() => { }));
        actions.FakeDeleteCommand.Returns(fakeDeleteCommand ?? new RelayCommand(() => { }));

        return actions;
    }
}
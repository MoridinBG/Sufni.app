using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class CommonButtonLineTests
{
    [AvaloniaFact]
    public async Task CommonButtonLine_SaveButton_BindsConfiguredCommand()
    {
        var viewModel = new TestEditorViewModel(canSave: () => false);
        var view = new CommonButtonLine
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var saveButton = view.FindControl<Button>("SaveButton");
            Assert.NotNull(saveButton);

            Assert.Same(viewModel.SaveCommand, saveButton!.Command);
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
        var viewModel = new TestEditorViewModel(canSave: () => true);
        var view = new CommonButtonLine
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var saveButton = view.FindControl<Button>("SaveButton");
            Assert.NotNull(saveButton);

            Assert.Same(viewModel.SaveCommand, saveButton!.Command);
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
        var viewModel = new TestEditorViewModel(onSave: () => saveInvoked = true);
        var view = new CommonButtonLine
        {
            DataContext = viewModel
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
        var shell = Substitute.For<IShellCoordinator>();
        var viewModel = new TestEditorViewModel(shell: shell);
        var view = new CommonButtonLine
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var backButton = view.FindControl<Button>("BackButton");
            Assert.NotNull(backButton);

            backButton!.Command!.Execute(backButton.CommandParameter);

            shell.Received(1).GoBack();
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
        var viewModel = new TestEditorViewModel(canReset: () => canReset);
        var view = new CommonButtonLine
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var resetButton = view.FindControl<Button>("ResetButton");
            Assert.NotNull(resetButton);

            Assert.Same(viewModel.ResetCommand, resetButton!.Command);
            Assert.False(resetButton.Command!.CanExecute(resetButton.CommandParameter));

            canReset = true;
            viewModel.ResetCommand.NotifyCanExecuteChanged();
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
    public async Task CommonButtonLine_DeleteButton_TracksCanExecuteChanges()
    {
        var canDelete = false;
        var viewModel = new TestEditorViewModel(canDelete: () => canDelete);
        var view = new CommonButtonLine
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var deleteButton = view.FindControl<Button>("DeleteButton");
            Assert.NotNull(deleteButton);

            Assert.False(deleteButton!.IsEnabled);

            canDelete = true;
            viewModel.RefreshDeleteState();
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.True(deleteButton.IsEnabled);
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
        bool? navigateBack = null;
        var viewModel = new TestEditorViewModel(onDelete: value => navigateBack = value);
        var view = new CommonButtonLine
        {
            DataContext = viewModel
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var deleteButton = view.FindControl<Button>("DeleteButton");
            Assert.NotNull(deleteButton);
            Assert.Null(deleteButton!.Command);

            var flyout = Assert.IsType<Flyout>(deleteButton!.Flyout);
            var flyoutContent = Assert.IsType<StackPanel>(flyout.Content);
            flyoutContent.DataContext = view.DataContext;
            await ViewTestHelpers.FlushDispatcherAsync();

            var innerDeleteButton = Assert.Single(flyoutContent.Children.OfType<Button>(), button => Equals(button.Content, "delete"));
            Assert.NotNull(innerDeleteButton.Command);

            innerDeleteButton.Command!.Execute(innerDeleteButton.CommandParameter);

            Assert.Same(viewModel.DeleteCommand, innerDeleteButton.Command);
            Assert.Equal(true, navigateBack);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    private sealed class TestEditorViewModel : TabPageViewModelBase
    {
        private readonly Action onSave;
        private readonly Action onReset;
        private readonly Action<bool> onDelete;
        private readonly Func<bool> canSave;
        private readonly Func<bool> canReset;
        private readonly Func<bool> canDelete;

        public TestEditorViewModel(
            Action? onSave = null,
            Action? onReset = null,
            Action<bool>? onDelete = null,
            Func<bool>? canSave = null,
            Func<bool>? canReset = null,
            Func<bool>? canDelete = null,
            IShellCoordinator? shell = null,
            IDialogService? dialogService = null)
            : base(shell ?? Substitute.For<IShellCoordinator>(), dialogService ?? Substitute.For<IDialogService>())
        {
            this.onSave = onSave ?? (() => { });
            this.onReset = onReset ?? (() => { });
            this.onDelete = onDelete ?? (_ => { });
            this.canSave = canSave ?? (() => true);
            this.canReset = canReset ?? (() => true);
            this.canDelete = canDelete ?? (() => true);
        }

        public void RefreshDeleteState() => NotifyDeleteCommandStateChanged();

        protected override bool CanSave() => canSave();

        protected override bool CanReset() => canReset();

        protected override bool CanDelete() => canDelete();

        protected override Task SaveImplementation()
        {
            onSave();
            return Task.CompletedTask;
        }

        protected override Task ResetImplementation()
        {
            onReset();
            return Task.CompletedTask;
        }

        protected override Task DeleteImplementation(bool navigateBack)
        {
            onDelete(navigateBack);
            return Task.CompletedTask;
        }
    }
}
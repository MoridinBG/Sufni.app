using Sufni.App.Shell.ViewModels.Base;
using Microsoft.Extensions.Time.Testing;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shared.Views.Controls;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Shared.Views.Controls;

[Collection("Ui")]
public class CommonButtonLineTests
{

    [AvaloniaFact]
    public async Task CommonButtonLine_DeleteButton_TracksCanExecuteChanges()
    {
        ViewTestHelpers.EnsureViewTestResources();
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

    private sealed class TestEditorViewModel(Func<bool> canDelete) : TabPageViewModelBase(
        Substitute.For<IShellCoordinator>(), Substitute.For<IDialogService>(),
        new InlineUiThreadDispatcher(), timeProvider: new FakeTimeProvider())
    {
        public void RefreshDeleteState() => NotifyDeleteCommandStateChanged();
        protected override bool CanDelete() => canDelete();
    }
}

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.ViewModels.Items;

public partial class ItemViewModelBase : TabPageViewModelBase
{
    #region Observable properties

    [ObservableProperty] private Guid id;
    [ObservableProperty] private DateTime? timestamp;
    [ObservableProperty] private bool isComplete = true;

    #endregion Observable properties

    #region Virtual methods / properties

    protected virtual bool CanDelete() { return true; }

    #endregion Virtual methods / properties

    #region Commands

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete(bool navigateBack)
    {
        var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");

        await mainPagesViewModel.DeleteItem(this);
        if (navigateBack)
        {
            OpenPreviousPage();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void UndoableDelete()
    {
        var mainPagesViewModel = App.Current?.Services?.GetService<MainPagesViewModel>();
        Debug.Assert(mainPagesViewModel != null, nameof(mainPagesViewModel) + " != null");

        mainPagesViewModel.UndoableDelete(this);

        Debug.Assert(App.Current is not null);
        if (App.Current.IsDesktop)
        {
            var vm = App.Current.Services?.GetService<MainWindowViewModel>();
            Debug.Assert(vm != null);
            vm.CloseTabPage(this);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void FakeDelete()
    {
        // This exists just so we can easily control the enabled/disabled
        // state of the Delete button on the CommonButtonLine.
    }

    #endregion Commands
}

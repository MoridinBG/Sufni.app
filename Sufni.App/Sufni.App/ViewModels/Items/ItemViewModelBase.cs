using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
        await ShellCoordinator.DeleteItem(this);
        if (navigateBack)
        {
            OpenPreviousPage();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void UndoableDelete()
    {
        ShellCoordinator.UndoableDelete(this);
        Navigator.CloseTab(this);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void FakeDelete()
    {
        // This exists just so we can easily control the enabled/disabled
        // state of the Delete button on the CommonButtonLine.
    }

    #endregion Commands
}

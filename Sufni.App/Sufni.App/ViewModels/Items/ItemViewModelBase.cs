using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Services;
using Sufni.App.ViewModels.Hosts;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.ViewModels.Items;

public partial class ItemViewModelBase : TabPageViewModelBase, IListItemRow
{
    // Explicit interface implementation: TabPageViewModelBase generates
    // OpenPageCommand as IRelayCommand<ViewModelBase>, which C# does not
    // accept as a covariant override of the interface's IRelayCommand
    // property. Forward to the generated command.
    IRelayCommand IListItemRow.OpenPageCommand => OpenPageCommand;

    #region Injected services

    protected readonly IItemDeletionHost deletionHost;

    #endregion Injected services

    #region Observable properties

    [ObservableProperty] private Guid id;
    [ObservableProperty] private DateTime? timestamp;
    [ObservableProperty] private bool isComplete = true;

    #endregion Observable properties

    #region Constructors

    protected ItemViewModelBase()
    {
        deletionHost = null!;
    }

    protected ItemViewModelBase(INavigator navigator, IDialogService dialogService, IItemDeletionHost deletionHost)
        : base(navigator, dialogService)
    {
        this.deletionHost = deletionHost;
    }

    #endregion Constructors

    #region Virtual methods / properties

    protected virtual bool CanDelete() { return true; }

    #endregion Virtual methods / properties

    #region Commands

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task Delete(bool navigateBack)
    {
        await deletionHost.Delete(this);
        if (navigateBack)
        {
            OpenPreviousPage();
        }
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void UndoableDelete()
    {
        deletionHost.UndoableDelete(this);
        navigator.CloseTab(this);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void FakeDelete()
    {
        // This exists just so we can easily control the enabled/disabled
        // state of the Delete button on the CommonButtonLine.
    }

    #endregion Commands
}

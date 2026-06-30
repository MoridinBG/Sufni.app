using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Shell.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    #region Observable properties

    public MainPagesViewModel MainPagesViewModel { get; }

    #endregion Observable properties

    #region Constructors

    public MainViewModel(
        MainPagesViewModel mainPagesViewModel,
        IMobileNavigationShellHost navigationHost,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
        MainPagesViewModel = mainPagesViewModel;
        navigationHost.SetRoot(mainPagesViewModel);
    }

    #endregion Constructors

    public bool TryCloseTransientShellSurface()
    {
        if (!MainPagesViewModel.IsDrawerOpen)
        {
            return false;
        }

        MainPagesViewModel.IsDrawerOpen = false;
        return true;
    }
}

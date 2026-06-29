using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Coordinators;
using Sufni.App.Services;

namespace Sufni.App.ViewModels;

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

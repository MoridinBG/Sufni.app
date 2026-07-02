using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Shell.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly IPlotZoomState plotZoomState;

    #region Observable properties

    public MainPagesViewModel MainPagesViewModel { get; }

    #endregion Observable properties

    #region Constructors

    public MainViewModel(
        MainPagesViewModel mainPagesViewModel,
        IMobileNavigationShellHost navigationHost,
        IPlotZoomState plotZoomState,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
        MainPagesViewModel = mainPagesViewModel;
        this.plotZoomState = plotZoomState;
        navigationHost.SetRoot(mainPagesViewModel);
    }

    #endregion Constructors

    public bool TryCloseTransientShellSurface()
    {
        if (plotZoomState.TryCollapse())
        {
            return true;
        }

        if (!MainPagesViewModel.IsDrawerOpen)
        {
            return false;
        }

        MainPagesViewModel.IsDrawerOpen = false;
        return true;
    }
}

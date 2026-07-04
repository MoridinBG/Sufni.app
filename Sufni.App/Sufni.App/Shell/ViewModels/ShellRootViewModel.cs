using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;

namespace Sufni.App.Shell.ViewModels;

public sealed class ShellRootViewModel : ViewModelBase
{
    private readonly IPlotZoomState plotZoomState;

    public ShellRootViewModel(
        MainPagesViewModel pages,
        ShellWorkspaceViewModel workspace,
        IAppEnvironment appEnvironment,
        IPlotZoomState plotZoomState,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
        Pages = pages;
        Workspace = workspace;
        LayoutProfile = appEnvironment.LayoutProfile;
        Capabilities = appEnvironment.Capabilities;
        this.plotZoomState = plotZoomState;
    }

    public MainPagesViewModel Pages { get; }
    public ShellWorkspaceViewModel Workspace { get; }
    public UiLayoutProfile LayoutProfile { get; }
    public AppCapabilities Capabilities { get; }

    public bool HandleBackRequest()
    {
        if (TryCloseTransientShellSurface())
        {
            return true;
        }

        return Workspace.GoBack();
    }

    public bool TryCloseTransientShellSurface()
    {
        if (plotZoomState.TryCollapse())
        {
            return true;
        }

        if (!Pages.IsDrawerOpen)
        {
            return false;
        }

        Pages.IsDrawerOpen = false;
        return true;
    }
}

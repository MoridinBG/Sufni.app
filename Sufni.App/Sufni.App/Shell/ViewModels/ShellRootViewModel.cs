using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;

namespace Sufni.App.Shell.ViewModels;

public sealed class ShellRootViewModel : ViewModelBase
{
    public ShellRootViewModel(
        MainPagesViewModel pages,
        ShellWorkspaceViewModel workspace,
        IAppEnvironment appEnvironment,
        IUiThreadDispatcher uiThreadDispatcher)
        : base(uiThreadDispatcher)
    {
        Pages = pages;
        Workspace = workspace;
        LayoutProfile = appEnvironment.LayoutProfile;
        Capabilities = appEnvironment.Capabilities;
    }

    public MainPagesViewModel Pages { get; }
    public ShellWorkspaceViewModel Workspace { get; }
    public UiLayoutProfile LayoutProfile { get; }
    public AppCapabilities Capabilities { get; }
}

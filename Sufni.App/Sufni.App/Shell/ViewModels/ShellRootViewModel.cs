using System.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;

namespace Sufni.App.Shell.ViewModels;

public sealed class ShellRootViewModel : ViewModelBase
{
    private readonly IPlotZoomState plotZoomState;
    private readonly IAppEnvironment appEnvironment;
    private UiLayoutProfile layoutProfile;
    private ShellRootPresentation presentation;

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
        this.appEnvironment = appEnvironment;
        layoutProfile = appEnvironment.LayoutProfile;
        presentation = new ShellRootPresentation(this, layoutProfile);
        Capabilities = appEnvironment.Capabilities;
        HasKeyboardInput = appEnvironment.Input.HasKeyboard;
        HasTouchInput = appEnvironment.Input.HasTouch;
        this.plotZoomState = plotZoomState;
        appEnvironment.PropertyChanged += OnAppEnvironmentPropertyChanged;
    }

    public MainPagesViewModel Pages { get; }
    public ShellWorkspaceViewModel Workspace { get; }
    public UiLayoutProfile LayoutProfile
    {
        get => layoutProfile;
        private set
        {
            if (SetProperty(ref layoutProfile, value))
            {
                Presentation = new ShellRootPresentation(this, value);
            }
        }
    }

    public ShellRootPresentation Presentation
    {
        get => presentation;
        private set => SetProperty(ref presentation, value);
    }

    public AppCapabilities Capabilities { get; }
    public bool HasKeyboardInput { get; }
    public bool HasTouchInput { get; }

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

    private void OnAppEnvironmentPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IAppEnvironment.LayoutProfile))
        {
            LayoutProfile = appEnvironment.LayoutProfile;
        }
    }
}

public sealed record ShellRootPresentation(ShellRootViewModel Root, UiLayoutProfile LayoutProfile);

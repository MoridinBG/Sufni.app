using Sufni.App.Shared.Models;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Reactive.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Base;

namespace Sufni.App.Shell.ViewModels;

public sealed partial class ShellRootViewModel : ViewModelBase, IDisposable
{
    private readonly IAppStartup startup;
    private readonly IAppShutdown shutdown;
    private readonly IDisposable startupSubscription;
    private readonly IPlotZoomState plotZoomState;
    private readonly IAppEnvironment appEnvironment;
    private UiLayoutProfile layoutProfile;
    private ShellRootPresentation presentation;

    public ShellRootViewModel(
        IAppStartup startup,
        IAppShutdown shutdown,
        ShellFeedbackViewModel feedback,
        ShellWorkspaceViewModel workspace,
        IAppEnvironment appEnvironment,
        IPlotZoomState plotZoomState,
        IUiThreadDispatcher uiThreadDispatcher,
        TimeProvider timeProvider)
        : base(uiThreadDispatcher, timeProvider: timeProvider)
    {
        this.startup = startup;
        Feedback = feedback;
        this.shutdown = shutdown;
        shutdown.RunningChanged += OnShutdownChanged;
        startupSubscription = startup.Observe().Subscribe(value => StartupState = value);
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

    [ObservableProperty] public partial MainPagesViewModel? Pages { get; internal set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(CanResetDatabase), nameof(CanQuit), nameof(StartupTitle), nameof(StartupMessage), nameof(StartupDetails))]
    [NotifyCanExecuteChangedFor(nameof(ResetDatabaseCommand), nameof(QuitCommand))]
    public partial ShellStartupState StartupState { get; private set; } = new ShellStartupState.Starting();

    public ShellFeedbackViewModel Feedback { get; }

    public bool IsShuttingDown => shutdown.IsRunning;
    private void OnShutdownChanged(object? sender, EventArgs args) => OnPropertyChanged(nameof(IsShuttingDown));

    public bool IsReady => StartupState is ShellStartupState.Ready;
    public bool CanResetDatabase => StartupState is ShellStartupState.Failed
        { Reason: StartupFailureReason.Database or StartupFailureReason.Reset };
    public bool CanQuit => StartupState is ShellStartupState.Failed or ShellStartupState.RelaunchRequired;
    public string LogPath => startup.LogPath;
    public string StartupTitle => StartupState switch
    {
        ShellStartupState.Failed => "Unable to start Sufni",
        ShellStartupState.RelaunchRequired => "Restart required",
        _ => "Starting Sufni"
    };
    public string StartupMessage => StartupState switch
    {
        ShellStartupState.Failed failed => FailurePresentation.From(failed.Reason, failed.Details).Summary,
        ShellStartupState.RelaunchRequired => "The database was reset. Quit and relaunch the application to continue.",
        _ => "Starting…"
    };
    public string? StartupDetails => (StartupState as ShellStartupState.Failed)?.Details;
    public event EventHandler? QuitRequested;

    public Task StartAsync(Func<MainPagesViewModel> createPages) =>
        startup.StartAsync(() => Pages = createPages());

    [RelayCommand(CanExecute = nameof(CanResetDatabase))]
    private Task ResetDatabase() => startup.ResetDatabaseAsync();

    [RelayCommand(CanExecute = nameof(CanQuit))]
    private void Quit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        startupSubscription.Dispose();
        shutdown.RunningChanged -= OnShutdownChanged;
        appEnvironment.PropertyChanged -= OnAppEnvironmentPropertyChanged;
    }

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

        if (Pages?.IsDrawerOpen != true)
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

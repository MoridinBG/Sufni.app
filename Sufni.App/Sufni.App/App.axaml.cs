using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Shared.Views;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Services;
using Sufni.App.Shell.Behaviors;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.Shell.KeyboardShortcuts;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Views;
using Sufni.App.Shared.Views.Input;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.SyncAndPairing.Coordinators;

namespace Sufni.App;

public partial class App : Application
{
    private readonly PlatformRoot root;
    private Task? shutdownRequest;
    private bool shutdownComplete;

    public App(PlatformRoot root)
    {
        this.root = root;
    }

    public new static App? Current => Application.Current as App;
    public IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        // Apply before XAML resolution to avoid a default-theme first frame.
        RequestedThemeVariant = SufniThemes.ToVariant(root.LaunchPreferences.ThemeMode);
        AvaloniaXamlLoader.Load(this);
        Resources[typeof(KeyboardShortcutRegistry)] = new KeyboardShortcutRegistry(root.Input);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ShouldUseDesignModePreviewStartup())
        {
            InitializeForDesignModePreview();
            base.OnFrameworkInitializationCompleted();
            return;
        }

        LoggingBootstrapper.InstallGlobalExceptionHooks();

        var services = new ServiceCollection().AddSharedServices(root);
        Services = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        if (!DataTemplates.OfType<ViewLocator>().Any())
        {
            DataTemplates.Add(Services.GetRequiredService<ViewLocator>());
        }

        var themeService = Services.GetRequiredService<IThemeService>();
        _ = themeService.InitializeAsync();

        var fileService = Services.GetRequiredService<IFilesService>();
        var dialogHost = Services.GetRequiredService<IDialogHost>();
        var input = Services.GetRequiredService<IAppEnvironment>().Input;
        var supportsTouchLongPressContextMenu =
            PointerGesture.SupportsTouchLongPressContextMenu(input);
        var hapticFeedback = Services.GetService<IHapticFeedback>();
        Action? longPressFeedback = hapticFeedback is null
            ? null
            : hapticFeedback.LongPress;

        var shellRoot = Services.GetRequiredService<ShellRootViewModel>();
        _ = Services.GetRequiredService<ShellDispatcherExceptionHandler>();
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                var mainWindow = new MainWindow(Services.GetService<IWindowBehavior>());
                UiTimers.SetScheduler(mainWindow, Services.GetRequiredService<IUiTimerScheduler>());
                PointerGesture.SetSupportsTouchLongPressContextMenu(
                    mainWindow,
                    supportsTouchLongPressContextMenu);
                HapticFeedbackBehavior.SetLongPressFeedback(mainWindow, longPressFeedback);
                desktop.MainWindow = mainWindow;
                mainWindow.Closing += async (_, args) =>
                {
                    if (shutdownComplete) return;
                    args.Cancel = true;
                    await RequestShutdownAsync();
                };
                desktop.ShutdownRequested += async (_, args) =>
                {
                    if (shutdownComplete) return;
                    args.Cancel = true;
                    await RequestShutdownAsync();
                };
                Services.GetRequiredService<IPlotZoomState>()
                    .SetSurface(mainWindow.FindControl<PlotZoomOverlayHost>("PlotZoomOverlay"));
                fileService.SetTarget(TopLevel.GetTopLevel(mainWindow));
                dialogHost.SetOwner(mainWindow);
                dialogHost.SetOverlayHost(mainWindow);
                dialogHost.SetPresentationMode(DialogPresentationMode.Window);
                mainWindow.DataContext = shellRoot;
                desktop.Exit += (_, _) => LoggingBootstrapper.FlushAndClose();
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                var mainView = new MainView
                {
                    DataContext = shellRoot
                };
                UiTimers.SetScheduler(mainView, Services.GetRequiredService<IUiTimerScheduler>());
                PointerGesture.SetSupportsTouchLongPressContextMenu(
                    mainView,
                    supportsTouchLongPressContextMenu);
                HapticFeedbackBehavior.SetLongPressFeedback(mainView, longPressFeedback);
                Services.GetRequiredService<IPlotZoomState>()
                    .SetSurface(mainView.FindControl<PlotZoomOverlayHost>("PlotZoomOverlay"));
                singleViewPlatform.MainView = mainView;
                if (singleViewPlatform.MainView is Control mainViewControl)
                {
                    dialogHost.SetOverlayHost(mainViewControl);
                    dialogHost.SetPresentationMode(DialogPresentationMode.Overlay);
                }
                singleViewPlatform.MainView.Loaded += (_, _) =>
                {
                    var topLevel = TopLevel.GetTopLevel(singleViewPlatform.MainView);
                    Debug.Assert(topLevel is not null);
                    topLevel.BackRequested += (_, e) =>
                    {
                        e.Handled = shellRoot.HandleBackRequest();
                    };
                    fileService.SetTarget(topLevel);
                };
                break;
        }

        shellRoot.QuitRequested += async (_, _) => await RequestShutdownAsync();

        _ = shellRoot.StartAsync(() =>
        {
            _ = Services.GetRequiredService<ITrackCoordinator>();
            _ = Services.GetRequiredService<IPairedDeviceCoordinator>();
            _ = Services.GetRequiredService<ISyncCoordinator>();
            ResolveCapabilityEagerServices(Services, root.Capabilities);
            return Services.GetRequiredService<MainPagesViewModel>();
        });

        base.OnFrameworkInitializationCompleted();
    }

    private Task RequestShutdownAsync()
    {
        if (shutdownComplete) return Task.CompletedTask;
        return shutdownRequest is { IsCompleted: false } ? shutdownRequest : shutdownRequest = ShutdownAsync();
    }

    private async Task ShutdownAsync()
    {
        // Finish the native close event before a synchronous drain can close the window again.
        await Task.Yield();
        var provider = (ServiceProvider)Services!;
        if (!await provider.GetRequiredService<IAppShutdown>().RequestAsync(async () =>
            {
                await provider.DisposeAsync();
                Services = null;
            }, LoggingBootstrapper.FlushAndClose)) return;

        shutdownComplete = true;
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }

    public Task OnBackgroundAsync() => Services?.GetRequiredService<IAppShutdown>()
        .OnBackgroundAsync(LoggingBootstrapper.Flush) ?? Task.CompletedTask;

    internal void InitializeForDesignModePreview()
    {
        if (!DataTemplates.OfType<ViewLocator>().Any())
        {
            DataTemplates.Add(new ViewLocator());
        }
    }

    internal bool ShouldUseDesignModePreviewStartup()
    {
        return Design.IsDesignMode
            || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
                and not ISingleViewApplicationLifetime;
    }

    internal static void ResolveCapabilityEagerServices(
        IServiceProvider services,
        AppCapabilities capabilities)
    {
        if (capabilities.CanPairAsClient)
        {
            // Resolve so DeviceId / IsPaired probe runs before the pairing screen is opened.
            _ = services.GetService<IPairingClientCoordinator>();
        }

        if (capabilities.CanHostSyncServer)
        {
            // Resolve so pairing and source-arrival subscriptions wire up before views load.
            _ = services.GetService<IPairingServerCoordinator>();
            services.GetRequiredService<SessionSyncApplier>().AttachServer(
                services.GetRequiredService<ISynchronizationServerService>());
        }
    }
}

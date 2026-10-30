using System;
using System.Diagnostics;
using System.Linq;
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

        // Services with constructor-time event subscriptions are
        // eagerly resolved here so the subscriptions are wired before any
        // sync, pairing, or telemetry arrival can happen.
        _ = Services.GetRequiredService<ISessionTrackReader>();
        _ = Services.GetRequiredService<IPairedDeviceCoordinator>();
        _ = Services.GetRequiredService<ISyncCoordinator>();

        ResolveCapabilityEagerServices(
            Services,
            Services.GetRequiredService<IAppEnvironment>().Capabilities);

        // One-time, per-device normalization: reset every source-backed session's
        // processing option to 25 ms and recompute it so its fingerprint records the
        // option it processed. Fire-and-forget off the UI thread; the pass waits for
        // database initialization itself and is resumable via its core_migration marker.
        _ = Services.GetRequiredService<ProcessingOptionsResetMigration>().RunAsync();
        _ = Services.GetRequiredService<RecordedSessionSourceRetentionCleanup>().RunAsync();

        var fileService = Services.GetRequiredService<IFilesService>();
        var dialogHost = Services.GetRequiredService<IDialogHost>();
        var input = Services.GetRequiredService<IAppEnvironment>().Input;
        var supportsTouchLongPressContextMenu =
            PointerGesture.SupportsTouchLongPressContextMenu(input);
        var hapticFeedback = Services.GetService<IHapticFeedback>();
        Action? longPressFeedback = hapticFeedback is null
            ? null
            : hapticFeedback.LongPress;

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                var desktopShellRootViewModel = Services.GetRequiredService<ShellRootViewModel>();
                var mainWindow = new MainWindow(Services.GetService<IWindowBehavior>());
                PointerGesture.SetSupportsTouchLongPressContextMenu(
                    mainWindow,
                    supportsTouchLongPressContextMenu);
                HapticFeedbackBehavior.SetLongPressFeedback(mainWindow, longPressFeedback);
                desktop.MainWindow = mainWindow;
                Services.GetRequiredService<IPlotZoomState>()
                    .SetSurface(mainWindow.FindControl<PlotZoomOverlayHost>("PlotZoomOverlay"));
                fileService.SetTarget(TopLevel.GetTopLevel(mainWindow));
                dialogHost.SetOwner(mainWindow);
                dialogHost.SetOverlayHost(mainWindow);
                dialogHost.SetPresentationMode(DialogPresentationMode.Window);
                mainWindow.DataContext = desktopShellRootViewModel;
                desktop.Exit += (_, _) => LoggingBootstrapper.FlushAndClose();
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                var shellRootViewModel = Services.GetRequiredService<ShellRootViewModel>();
                var mainView = new MainView
                {
                    DataContext = shellRootViewModel
                };
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
                        e.Handled = shellRootViewModel.HandleBackRequest();
                    };
                    fileService.SetTarget(topLevel);
                };
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }

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
            // Resolve so pairing and inbound-sync event subscriptions wire up before views load.
            _ = services.GetService<IPairingServerCoordinator>();
            _ = services.GetService<IInboundSyncCoordinator>();
            _ = services.GetService<SessionSyncApplier>();
        }
    }
}

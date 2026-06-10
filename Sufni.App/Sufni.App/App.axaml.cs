using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sufni.App.Coordinators;
using Sufni.App.Queries;
using Sufni.App.SessionGraph;
using Sufni.App.Services;
using Sufni.App.Services.Management;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;
using Sufni.App.Theming;
using Sufni.App.ViewModels;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.Views;
using Sufni.App.DesktopViews;
using System;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Sufni.App.ExtensionHost;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.ExtensionHost.Sync;
using Sufni.App.ExtensionHost.Services;
using Sufni.App.ExtensionHosting;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.ExtensionHosting.RecordedSessions;
using Sufni.App.ExtensionHosting.Sync;
using Sufni.App.Models;

namespace Sufni.App;

public partial class App : Application
{
    internal static IServiceCollection ServiceCollection { get; } = new ServiceCollection();
    internal static AppExtensionCollection Extensions { get; } = new();

    public new static App? Current => Application.Current as App;
    public IServiceProvider? Services { get; private set; }
    public bool IsDesktop { get; private set; }

    internal void SetIsDesktopForTests(bool isDesktop)
    {
        IsDesktop = isDesktop;
    }

    public override void Initialize()
    {
        // Read the persisted theme mode before XAML loads so the first frame
        // renders the right variant. Without this, the user briefly sees the
        // dark variant set in App.axaml before the theme service applies the
        // persisted choice.
        ThemeBootstrap.ApplyPersistedVariant(this);

        AvaloniaXamlLoader.Load(this);

#if DEBUG
        if (!OperatingSystem.IsIOS() && !OperatingSystem.IsAndroid())
        {
            this.AttachDeveloperTools();
        }
#endif
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

        var isDesktop = ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;

        RegisterBuildTimeExtensions(Extensions, isDesktop);

        var extensionViewRegistry = new ExtensionViewRegistry();
        ServiceCollection.AddSingleton<IExtensionViewRegistry>(extensionViewRegistry);

        var extensionServiceContext = new AppExtensionServiceRegistrationContext(isDesktop);
        Extensions.RegisterServices(ServiceCollection, extensionServiceContext);

        var extensionCapabilityRegistry = new AppExtensionCapabilityRegistry(extensionViewRegistry);
        ServiceCollection.AddSingleton(extensionCapabilityRegistry);
        ServiceCollection.AddSingleton<IAppExtensionCapabilityRegistry>(extensionCapabilityRegistry);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            ServiceCollection.AddSingleton<IMainWindowShellHost>(sp =>
                sp.GetRequiredService<MainWindowViewModel>());
            ServiceCollection.AddSingleton<IShellCoordinator>(sp =>
                new DesktopShellCoordinator(() => sp.GetRequiredService<IMainWindowShellHost>()));
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            ServiceCollection.AddSingleton<IMainViewShellHost>(sp =>
                sp.GetRequiredService<MainViewModel>());
            ServiceCollection.AddSingleton<IShellCoordinator>(sp =>
                new MobileShellCoordinator(() => sp.GetRequiredService<IMainViewShellHost>()));
        }

        ServiceCollection.AddSingleton<IHttpApiService, HttpApiService>();
        ServiceCollection.AddSingleton<ViewLocator>(sp => new ViewLocator(
            sp.GetRequiredService<IExtensionViewRegistry>(),
            sp));
        ServiceCollection.AddSingleton<IBackgroundTaskRunner, BackgroundTaskRunner>();
        ServiceCollection.AddSingleton<IUiThreadDispatcher, AvaloniaUiThreadDispatcher>();
        ServiceCollection.AddSingleton<IBikeEditorService, BikeEditorService>();
        ServiceCollection.AddSingleton<ISessionPresentationService, SessionPresentationService>();
        ServiceCollection.AddSingleton<ISessionAnalysisService, SessionAnalysisService>();
        ServiceCollection.AddSingleton<ISessionTelemetryProcessor, SessionTelemetryProcessor>();
        ServiceCollection.AddSingleton<IDaqManagementService, DaqManagementService>();
        ServiceCollection.AddSingleton<ITelemetryDataStoreService, TelemetryDataStoreService>();
        ServiceCollection.AddSingleton<SqliteConnectionContext>(sp =>
            new SqliteConnectionContext(
                AppPaths.DatabasePath,
                createAppDirectories: true,
                sp.GetServices<IExtensionDatabaseMigrator>().ToArray(),
                sp.GetServices<IExtensionCascadeRuleProvider>().ToArray(),
                () => sp.GetServices<IExtensionStateRefreshParticipant>().ToArray()));
        ServiceCollection.AddSingleton(typeof(ISynchronizableRepository<>), typeof(SynchronizableRepository<>));
        ServiceCollection.AddSingleton<IPairedDeviceRepository, PairedDeviceRepository>();
        ServiceCollection.AddSingleton<IRecordedSessionSourceRepository, RecordedSessionSourceRepository>();
        ServiceCollection.AddSingleton<ISessionCacheStore, SessionCacheStore>();
        ServiceCollection.AddSingleton<ITrackRepository, TrackRepository>();
        ServiceCollection.AddSingleton<ISessionRepository, SessionRepository>();
        ServiceCollection.AddSingleton<ISyncDataStore, SynchronizationMergeEngine>();
        ServiceCollection.AddSingleton<IExtensionDatabaseConnection, ExtensionDatabaseConnection>();
        ServiceCollection.AddSingleton<IRecordedSessionDataReader, RecordedSessionDataReader>();
        ServiceCollection.AddSingleton<IExtensionNotificationService, ExtensionNotificationService>();
        ServiceCollection.AddSingleton<IExtensionCascadeService, ExtensionCascadeService>();
        ServiceCollection.AddSingleton<IExtensionSyncService, ExtensionSyncService>();
        ServiceCollection.TryAddSingleton<IRecordedSessionListExtensionService, RecordedSessionListExtensionService>();
        ServiceCollection.AddSingleton<IAppPreferences, AppPreferences>();
        ServiceCollection.AddSingleton<IThemeService, ThemeService>();
        ServiceCollection.AddSingleton<IMapPreferences>(sp => sp.GetRequiredService<IAppPreferences>().Map);
        ServiceCollection.AddSingleton<ISessionPreferences>(sp => sp.GetRequiredService<IAppPreferences>().Session);
        ServiceCollection.AddSingleton<ITileLayerService, TileLayerService>();
        ServiceCollection.AddSingleton<FilesService>();
        ServiceCollection.AddSingleton<IFilesService>(sp => sp.GetRequiredService<FilesService>());
        ServiceCollection.AddSingleton<IFilePickerService>(sp => sp.GetRequiredService<FilesService>());
        ServiceCollection.AddSingleton<DialogService>();
        ServiceCollection.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        ServiceCollection.AddSingleton<IExtensionDialogService>(sp => sp.GetRequiredService<DialogService>());
        ServiceCollection.AddSingleton<BikeStore>();
        ServiceCollection.AddSingleton<IBikeStore>(sp => sp.GetRequiredService<BikeStore>());
        ServiceCollection.AddSingleton<IBikeStoreWriter>(sp => sp.GetRequiredService<BikeStore>());
        ServiceCollection.AddSingleton<IBikeDependencyQuery, BikeDependencyQuery>();
        ServiceCollection.AddSingleton<ILiveDaqKnownBoardsQuery, LiveDaqKnownBoardsQuery>();
        ServiceCollection.AddSingleton<BikeCoordinator>();
        ServiceCollection.AddSingleton<IBikeCoordinator>(sp => sp.GetRequiredService<BikeCoordinator>());
        ServiceCollection.AddSingleton<SetupStore>();
        ServiceCollection.AddSingleton<ISetupStore>(sp => sp.GetRequiredService<SetupStore>());
        ServiceCollection.AddSingleton<ISetupStoreWriter>(sp => sp.GetRequiredService<SetupStore>());
        ServiceCollection.AddSingleton<SetupCoordinator>();
        ServiceCollection.AddSingleton<ISetupCoordinator>(sp => sp.GetRequiredService<SetupCoordinator>());
        ServiceCollection.AddSingleton<SessionStore>();
        ServiceCollection.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<SessionStore>());
        ServiceCollection.AddSingleton<ISessionStoreWriter>(sp => sp.GetRequiredService<SessionStore>());
        ServiceCollection.AddSingleton<RecordedSessionSourceStore>();
        ServiceCollection.AddSingleton<IRecordedSessionSourceStore>(sp => sp.GetRequiredService<RecordedSessionSourceStore>());
        ServiceCollection.AddSingleton<IRecordedSessionSourceStoreWriter>(sp => sp.GetRequiredService<RecordedSessionSourceStore>());
        ServiceCollection.AddSingleton<IProcessingFingerprintService, ProcessingFingerprintService>();
        ServiceCollection.AddSingleton<IRecordedSessionDomainQuery, RecordedSessionDomainQuery>();
        ServiceCollection.AddSingleton<IRecordedSessionGraph, RecordedSessionGraph>();
        ServiceCollection.AddSingleton<IRecordedSessionReprocessor, RecordedSessionReprocessor>();
        ServiceCollection.AddSingleton<TrackCoordinator>();
        ServiceCollection.AddSingleton<SessionCoordinator>(sp => new SessionCoordinator(
            sp.GetRequiredService<ISessionStoreWriter>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<IRecordedSessionSourceRepository>(),
            sp.GetRequiredService<ISynchronizableRepository<Setup>>(),
            sp.GetRequiredService<ISynchronizableRepository<Bike>>(),
            sp.GetRequiredService<ISynchronizableRepository<Track>>(),
            sp.GetRequiredService<ISynchronizableRepository<Session>>(),
            sp.GetRequiredService<ISessionCacheStore>(),
            sp.GetRequiredService<IHttpApiService>(),
            sp.GetRequiredService<IBackgroundTaskRunner>(),
            sp.GetRequiredService<TrackCoordinator>(),
            sp.GetRequiredService<ISessionPresentationService>(),
            sp.GetRequiredService<ISessionAnalysisService>(),
            sp.GetRequiredService<ITileLayerService>(),
            sp.GetRequiredService<ISessionPreferences>(),
            sp.GetRequiredService<IShellCoordinator>(),
            sp.GetRequiredService<IDialogService>(),
            sp.GetRequiredService<IUiThreadDispatcher>(),
            sp.GetRequiredService<IRecordedSessionSourceStoreWriter>(),
            sp.GetRequiredService<IRecordedSessionDomainQuery>(),
            sp.GetRequiredService<IRecordedSessionGraph>(),
            sp.GetRequiredService<IRecordedSessionReprocessor>(),
            sp.GetRequiredService<IRecordedSessionDataReader>(),
            sp.GetService<ISynchronizationServerService>(),
            sp.GetRequiredService<IBikeCoordinator>(),
            sp.GetRequiredService<IExtensionCascadeService>(),
            sp.GetServices<IRecordedSessionExtensionFactory>(),
            sp.GetRequiredService<IExtensionDatabaseConnection>()));
        ServiceCollection.AddSingleton<ISessionCoordinator>(sp => sp.GetRequiredService<SessionCoordinator>());
        ServiceCollection.AddSingleton<LiveDaqStore>();
        ServiceCollection.AddSingleton<ILiveDaqStore>(sp => sp.GetRequiredService<LiveDaqStore>());
        ServiceCollection.AddSingleton<ILiveDaqStoreWriter>(sp => sp.GetRequiredService<LiveDaqStore>());
        ServiceCollection.AddSingleton<IDaqBrowseOwner, DaqBrowseOwner>();
        ServiceCollection.AddSingleton<ILiveDaqBoardIdInspector, LiveDaqBoardIdInspector>();
        ServiceCollection.AddSingleton<ILiveDaqCatalogService, LiveDaqCatalogService>();
        ServiceCollection.AddSingleton<Func<ILiveDaqClient>>(_ => static () => new LiveDaqClient());
        ServiceCollection.AddSingleton<ILiveDaqSharedStreamRegistry, LiveDaqSharedStreamRegistry>();
        ServiceCollection.AddSingleton<LiveGraphPipelineFactory>();
        ServiceCollection.AddSingleton<ILiveSessionServiceFactory, LiveSessionServiceFactory>();
        ServiceCollection.AddSingleton<LiveDaqCoordinator>();
        ServiceCollection.AddSingleton<PairedDeviceStore>();
        ServiceCollection.AddSingleton<IPairedDeviceStore>(sp => sp.GetRequiredService<PairedDeviceStore>());
        ServiceCollection.AddSingleton<IPairedDeviceStoreWriter>(sp => sp.GetRequiredService<PairedDeviceStore>());
        ServiceCollection.AddSingleton<PairedDeviceCoordinator>();
        ServiceCollection.AddSingleton<SyncCoordinator>();
        ServiceCollection.AddSingleton<ImportSessionsCoordinator>(sp =>
            new ImportSessionsCoordinator(
                sp.GetRequiredService<ISessionRepository>(),
                sp.GetRequiredService<ISynchronizableRepository<Setup>>(),
                sp.GetRequiredService<ISynchronizableRepository<Bike>>(),
                sp.GetRequiredService<ISessionStoreWriter>(),
                sp.GetRequiredService<IRecordedSessionSourceStoreWriter>(),
                sp.GetRequiredService<IShellCoordinator>(),
                sp.GetRequiredService<IBackgroundTaskRunner>(),
                sp.GetRequiredService<IUiThreadDispatcher>(),
                sp.GetRequiredService<IDaqManagementService>(),
                sp.GetRequiredService<IRecordedSessionReprocessor>(),
                () => sp.GetRequiredService<ImportSessionsViewModel>()));
        ServiceCollection.AddSingleton<BikeListViewModel>();
        ServiceCollection.AddSingleton<SessionListViewModel>();
        ServiceCollection.AddSingleton<LiveDaqListViewModel>();
        ServiceCollection.AddSingleton<PairedDeviceListViewModel>();
        ServiceCollection.AddSingleton<ImportSessionsViewModel>();
        ServiceCollection.AddSingleton<SetupListViewModel>();
        ServiceCollection.AddSingleton<MainPagesViewModel>();
        ServiceCollection.AddSingleton<WelcomeScreenViewModel>();
        ServiceCollection.AddSingleton<MainViewModel>();
        ServiceCollection.AddSingleton<MainWindowViewModel>();

        Extensions.RegisterCapabilities(extensionCapabilityRegistry);

        IsDesktop = isDesktop;
        Services = ServiceCollection.BuildServiceProvider();

        if (!DataTemplates.OfType<ViewLocator>().Any())
        {
            DataTemplates.Add(Services.GetRequiredService<ViewLocator>());
        }

        // Resolve the theme service early so its constructor reads the
        // bootstrap-applied RequestedThemeVariant. InitializeAsync reconciles
        // against the persisted JSON if it diverges (e.g. file written after
        // ThemeBootstrap ran).
        var themeService = Services.GetRequiredService<IThemeService>();
        _ = themeService.InitializeAsync();

        // Coordinators with constructor-time event subscriptions are
        // eagerly resolved here so the subscriptions are wired before any
        // sync, pairing, or telemetry arrival can happen.
        _ = Services.GetRequiredService<SessionCoordinator>();
        _ = Services.GetRequiredService<PairedDeviceCoordinator>();
        _ = Services.GetRequiredService<SyncCoordinator>();

        // Mobile-only: eagerly resolve so DeviceId / IsPaired probe runs
        // before the pairing screen is opened.
        if (!IsDesktop)
        {
            _ = Services.GetService<IPairingClientCoordinator>();
        }

        // Desktop-only: eagerly resolve so the constructor's
        // PairingRequested/PairingConfirmed event subscriptions wire up
        // before the desktop view loads.
        if (IsDesktop)
        {
            _ = Services.GetService<IPairingServerCoordinator>();
            _ = Services.GetService<IInboundSyncCoordinator>();
        }

        foreach (var eagerServiceType in extensionCapabilityRegistry.EagerServiceTypes)
        {
            _ = Services.GetRequiredService(eagerServiceType);
        }

        var fileService = Services.GetRequiredService<IFilesService>();
        var dialogService = Services.GetRequiredService<IDialogService>();
        var mainViewModel = Services.GetRequiredService<MainViewModel>();
        var mainWindowViewModel = Services.GetRequiredService<MainWindowViewModel>();

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow();
                fileService.SetTarget(TopLevel.GetTopLevel(desktop.MainWindow));
                dialogService.SetOwner(desktop.MainWindow);
                dialogService.SetOverlayHost(desktop.MainWindow);
                desktop.MainWindow.DataContext = mainWindowViewModel;
                desktop.Exit += (_, _) => LoggingBootstrapper.FlushAndClose();
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                singleViewPlatform.MainView = new MainView
                {
                    DataContext = mainViewModel
                };
                if (singleViewPlatform.MainView is Control mainView)
                {
                    dialogService.SetOverlayHost(mainView);
                }
                singleViewPlatform.MainView.Loaded += (_, _) =>
                {
                    var topLevel = TopLevel.GetTopLevel(singleViewPlatform.MainView);
                    Debug.Assert(topLevel is not null);
                    topLevel.BackRequested += (_, e) =>
                    {
                        mainViewModel.OpenPreviousView();
                        e.Handled = true;
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

    static partial void RegisterBuildTimeExtensions(AppExtensionCollection extensions, bool isDesktop);
}

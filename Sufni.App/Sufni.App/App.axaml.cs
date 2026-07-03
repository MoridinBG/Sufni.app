using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Sync;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Extensibility.Capabilities;
using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.ItemLists;
using Sufni.App.Extensibility.Database;
using Sufni.App.Extensibility.Notifications;
using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.Extensibility.Sync;
using Sufni.App.Extensibility.Views;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.Infrastructure;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.LiveDaq.ViewModels.ItemLists;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.Services;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Insights.Services;
using Sufni.App.Sessions.Insights.Services.SessionInsights;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Lists.ViewModels.ItemLists;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models;
using Sufni.App.Setups.Stores;
using Sufni.App.Setups.ViewModels.ItemLists;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.DesktopViews;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Views;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.SyncAndPairing.ViewModels.ItemLists;
namespace Sufni.App;

public partial class App : Application
{
    internal static IServiceCollection ServiceCollection { get; } = new ServiceCollection();
    internal static AppExtensionCollection Extensions { get; } = new();

    public new static App? Current => Application.Current as App;
    public IServiceProvider? Services { get; private set; }
    public bool IsDesktop { get; private set; }

#if DEBUG
    protected virtual bool ShouldAttachDeveloperTools => !OperatingSystem.IsIOS() && !OperatingSystem.IsAndroid();
#endif

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
        if (ShouldAttachDeveloperTools)
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
            ServiceCollection.AddSingleton<ISessionLayoutStrategy, DesktopSessionLayoutStrategy>();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            ServiceCollection.AddSingleton<MobileNavigationShellHost>();
            ServiceCollection.AddSingleton<IMobileNavigationShellHost>(sp =>
                sp.GetRequiredService<MobileNavigationShellHost>());
            ServiceCollection.AddSingleton<IMobileNavigationPageHost>(sp =>
                sp.GetRequiredService<MobileNavigationShellHost>());
            ServiceCollection.AddSingleton<IShellCoordinator>(sp =>
                new MobileShellCoordinator(sp.GetRequiredService<IMobileNavigationShellHost>()));
            ServiceCollection.AddSingleton<ISessionLayoutStrategy, MobileSessionLayoutStrategy>();
        }

        ServiceCollection.AddSingleton<IHttpApiService, HttpApiService>();
        ServiceCollection.AddSingleton<ViewLocator>(sp => new ViewLocator(
            sp.GetRequiredService<IExtensionViewRegistry>(),
            sp));
        ServiceCollection.AddSingleton<IBackgroundTaskRunner, BackgroundTaskRunner>();
        ServiceCollection.AddSingleton<IUiThreadDispatcher, AvaloniaUiThreadDispatcher>();
        ServiceCollection.AddSingleton<IKinematicSolutionCache, KinematicSolutionCache>();
        ServiceCollection.AddSingleton<IRearTravelCalibrationBuilder, RearTravelCalibrationBuilder>();
        ServiceCollection.AddSingleton<IBikeRearSuspensionValidator, BikeRearSuspensionValidator>();
        ServiceCollection.AddSingleton<ITelemetryBikeProcessingContextFactory, TelemetryBikeProcessingContextFactory>();
        ServiceCollection.AddSingleton<IBikeEditorService, BikeEditorService>();
        ServiceCollection.AddSingleton<ISessionPresentationService, SessionPresentationService>();
        ServiceCollection.AddSingleton<ISessionInsightsService, SessionInsightsService>();
        ServiceCollection.AddSingleton<IRecordedSessionAnalysisComputer, RecordedSessionAnalysisComputer>();
        ServiceCollection.AddTransient<IRecordedSessionAnalysisResultStateFactory, RecordedSessionAnalysisResultStateFactory>();
        ServiceCollection.AddSingleton<ISessionTelemetryProcessor, SessionTelemetryProcessor>();
        ServiceCollection.AddSingleton<ISessionProcessedTelemetryReader, SessionProcessedTelemetryReader>();
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
        ServiceCollection.AddSingleton<ISessionTrackReader, SessionTrackReader>();
        ServiceCollection.AddSingleton<IFullTrackPointReader, FullTrackPointReader>();
        ServiceCollection.AddSingleton<ISessionRepository, SessionRepository>();
        ServiceCollection.AddSingleton<ISessionTelemetryWriter, SessionTelemetryWriter>();
        ServiceCollection.AddSingleton<ISessionBlobSwapRequestStore, SessionBlobSwapRequestStore>();
        ServiceCollection.AddSingleton<ISyncDataStore, SynchronizationMergeEngine>();
        ServiceCollection.AddSingleton<IExtensionDatabaseConnection, ExtensionDatabaseConnection>();
        ServiceCollection.AddSingleton<IRecordedSessionDataReader, RecordedSessionDataReader>();
        ServiceCollection.AddSingleton<IExtensionNotificationService, ExtensionNotificationService>();
        ServiceCollection.AddSingleton<IExtensionCascadeService, ExtensionCascadeService>();
        ServiceCollection.AddSingleton<IExtensionSyncService, ExtensionSyncService>();
        ServiceCollection.TryAddSingleton<IRecordedSessionListExtensionService, RecordedSessionListExtensionService>();
        ServiceCollection.TryAddSingleton<IRecordedSessionDerivationWindowProvider, NullRecordedSessionDerivationWindowProvider>();
        ServiceCollection.AddSingleton<IAppPreferences, AppPreferences>();
        ServiceCollection.AddSingleton<IThemeService, ThemeService>();
        ServiceCollection.AddSingleton<IMapPreferences>(sp => sp.GetRequiredService<IAppPreferences>().Map);
        ServiceCollection.AddSingleton<ISessionPreferences>(sp => sp.GetRequiredService<IAppPreferences>().Session);
        ServiceCollection.AddSingleton<IUiPreferences>(sp => sp.GetRequiredService<IAppPreferences>().Ui);
        ServiceCollection.AddSingleton<ITileLayerService, TileLayerService>();
        ServiceCollection.AddSingleton<IMapViewModelFactory, MapViewModelFactory>();
        ServiceCollection.AddSingleton<FilesService>();
        ServiceCollection.AddSingleton<IFilesService>(sp => sp.GetRequiredService<FilesService>());
        ServiceCollection.AddSingleton<IFilePickerService>(sp => sp.GetRequiredService<FilesService>());
        ServiceCollection.AddSingleton<DialogService>();
        ServiceCollection.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        ServiceCollection.AddSingleton<IDialogHost>(sp => sp.GetRequiredService<DialogService>());
        ServiceCollection.AddSingleton<IExtensionDialogService>(sp => sp.GetRequiredService<DialogService>());
        ServiceCollection.AddSingleton<IPlotZoomState, PlotZoomState>();
        ServiceCollection.AddSingleton<BikeStore>();
        ServiceCollection.AddSingleton<IBikeStore>(sp => sp.GetRequiredService<BikeStore>());
        ServiceCollection.AddSingleton<IBikeStoreWriter>(sp => sp.GetRequiredService<BikeStore>());
        ServiceCollection.AddSingleton<IBikeDependencyQuery, BikeDependencyQuery>();
        ServiceCollection.AddSingleton<ILiveDaqKnownBoardsQuery, LiveDaqKnownBoardsQuery>();
        ServiceCollection.AddSingleton<Func<ImportSessionsViewModel>>(sp =>
            () => sp.GetRequiredService<ImportSessionsViewModel>());
        ServiceCollection.AddSingleton<IEditorFactory, EditorFactory>();
        ServiceCollection.AddSingleton<Func<IEditorFactory>>(sp => () => sp.GetRequiredService<IEditorFactory>());
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
        ServiceCollection.AddSingleton<IAppDataRefresher, AppDataRefresher>();
        ServiceCollection.AddSingleton<IProcessingFingerprintService, ProcessingFingerprintService>();
        ServiceCollection.AddSingleton<IRecordedSessionProcessingOptionCache, RecordedSessionProcessingOptionCache>();
        ServiceCollection.AddSingleton<IProcessingDependencyHashIndex, ProcessingDependencyHashIndex>();
        ServiceCollection.AddSingleton<IRecordedSessionDerivationWindowCache, RecordedSessionDerivationWindowCache>();
        ServiceCollection.AddSingleton<IRecordedSessionDomainQuery, RecordedSessionDomainQuery>();
        ServiceCollection.AddSingleton<IRecordedSessionProjection, RecordedSessionProjection>();
        ServiceCollection.AddSingleton<IRecordedSessionReprocessor, RecordedSessionReprocessor>();
        ServiceCollection.AddSingleton<IRecordedSessionSourceSyncQuery, RecordedSessionSourceSyncQuery>();
        ServiceCollection.AddSingleton<TrackCoordinator>();
        ServiceCollection.AddSingleton<ITrackCoordinator>(sp => sp.GetRequiredService<TrackCoordinator>());
        ServiceCollection.AddSingleton<SessionLoader>(sp => new SessionLoader(
            sp.GetRequiredService<ISessionStoreWriter>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<ISessionTelemetryWriter>(),
            sp.GetRequiredService<ISessionProcessedTelemetryReader>(),
            sp.GetRequiredService<ISessionCacheStore>(),
            sp.GetRequiredService<IHttpApiService>(),
            sp.GetRequiredService<IBackgroundTaskRunner>(),
            sp.GetRequiredService<ITrackCoordinator>(),
            sp.GetRequiredService<ISessionPresentationService>(),
            sp.GetRequiredService<IRecordedSessionDomainQuery>()));
        ServiceCollection.AddSingleton<ISessionRecomputeEngine>(sp => new SessionRecomputeEngine(
            sp.GetRequiredService<ISessionStoreWriter>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<ISessionTelemetryWriter>(),
            sp.GetRequiredService<ISynchronizableRepository<Track>>(),
            sp.GetRequiredService<IBackgroundTaskRunner>(),
            sp.GetRequiredService<IRecordedSessionProcessingOptionCache>(),
            sp.GetRequiredService<IRecordedSessionSourceStoreWriter>(),
            sp.GetRequiredService<IRecordedSessionDomainQuery>(),
            sp.GetRequiredService<IRecordedSessionReprocessor>()));
        ServiceCollection.AddSingleton<SessionCommandService>(sp => new SessionCommandService(
            sp.GetRequiredService<ISessionStoreWriter>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<ISessionTelemetryWriter>(),
            sp.GetRequiredService<ISynchronizableRepository<Setup>>(),
            sp.GetRequiredService<ISynchronizableRepository<Bike>>(),
            sp.GetRequiredService<ISynchronizableRepository<Track>>(),
            sp.GetRequiredService<ISynchronizableRepository<Session>>(),
            sp.GetRequiredService<IRecordedSessionSourceRepository>(),
            sp.GetRequiredService<IRecordedSessionSourceStoreWriter>(),
            sp.GetRequiredService<IRecordedSessionReprocessor>(),
            sp.GetRequiredService<IBackgroundTaskRunner>(),
            sp.GetRequiredService<ISessionPreferences>(),
            sp.GetRequiredService<IShellCoordinator>(),
            sp.GetRequiredService<ISessionRecomputeEngine>(),
            sp.GetRequiredService<Func<IEditorFactory>>(),
            sp.GetRequiredService<IRecordedSessionDerivationWindowCache>(),
            sp.GetRequiredService<IRecordedSessionDerivationWindowProvider>()));
        ServiceCollection.AddSingleton<SessionSyncApplier>(sp => new SessionSyncApplier(
            sp.GetRequiredService<ISessionStoreWriter>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<IRecordedSessionSourceRepository>(),
            sp.GetRequiredService<IRecordedSessionSourceStoreWriter>(),
            sp.GetRequiredService<IUiThreadDispatcher>(),
            sp.GetService<ISynchronizationServerService>()));
        ServiceCollection.AddSingleton<ISessionCoordinator, SessionCoordinator>();
        ServiceCollection.AddSingleton<ProcessingOptionsResetMigration>(sp => new ProcessingOptionsResetMigration(
            sp.GetRequiredService<SqliteConnectionContext>(),
            sp.GetRequiredService<IRecordedSessionSourceRepository>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<IAppDataRefresher>(),
            sp.GetRequiredService<ISessionPreferences>(),
            sp.GetRequiredService<IRecordedSessionProcessingOptionCache>(),
            sp.GetRequiredService<ISessionRecomputeEngine>(),
            sp.GetRequiredService<IBackgroundTaskRunner>()));
        ServiceCollection.AddSingleton<RecordedSessionSourceRetentionCleanup>(sp => new RecordedSessionSourceRetentionCleanup(
            sp.GetRequiredService<SqliteConnectionContext>(),
            sp.GetRequiredService<IRecordedSessionSourceRepository>(),
            sp.GetRequiredService<IRecordedSessionDerivationWindowProvider>(),
            sp.GetRequiredService<IBackgroundTaskRunner>()));
        ServiceCollection.AddSingleton<LiveDaqStore>();
        ServiceCollection.AddSingleton<ILiveDaqStore>(sp => sp.GetRequiredService<LiveDaqStore>());
        ServiceCollection.AddSingleton<ILiveDaqStoreWriter>(sp => sp.GetRequiredService<LiveDaqStore>());
        ServiceCollection.AddSingleton<IDaqBrowseOwner, DaqBrowseOwner>();
        ServiceCollection.AddSingleton<ILiveDaqBoardIdInspector, LiveDaqBoardIdInspector>();
        ServiceCollection.AddSingleton<ILiveDaqCatalogService, LiveDaqCatalogService>();
        ServiceCollection.AddSingleton<ILiveDaqClientFactory, LiveDaqClientFactory>();
        ServiceCollection.AddSingleton<ILiveDaqSharedStreamRegistry, LiveDaqSharedStreamRegistry>();
        ServiceCollection.AddSingleton<LiveSignalPipelineFactory>();
        ServiceCollection.AddSingleton<ILiveSessionServiceFactory, LiveSessionServiceFactory>();
        ServiceCollection.AddSingleton<LiveDaqCoordinator>();
        ServiceCollection.AddSingleton<ILiveDaqCoordinator>(sp => sp.GetRequiredService<LiveDaqCoordinator>());
        ServiceCollection.AddSingleton<PairedDeviceStore>();
        ServiceCollection.AddSingleton<IPairedDeviceStore>(sp => sp.GetRequiredService<PairedDeviceStore>());
        ServiceCollection.AddSingleton<IPairedDeviceStoreWriter>(sp => sp.GetRequiredService<PairedDeviceStore>());
        ServiceCollection.AddSingleton<PairedDeviceCoordinator>();
        ServiceCollection.AddSingleton<IPairedDeviceCoordinator>(sp => sp.GetRequiredService<PairedDeviceCoordinator>());
        ServiceCollection.AddSingleton<SyncCoordinator>();
        ServiceCollection.AddSingleton<ISyncCoordinator>(sp => sp.GetRequiredService<SyncCoordinator>());
        ServiceCollection.AddSingleton<ImportSessionsCoordinator>(sp =>
            new ImportSessionsCoordinator(
                sp.GetRequiredService<ISessionTelemetryWriter>(),
                sp.GetRequiredService<ISynchronizableRepository<Setup>>(),
                sp.GetRequiredService<ISynchronizableRepository<Bike>>(),
                sp.GetRequiredService<ISessionStoreWriter>(),
                sp.GetRequiredService<IRecordedSessionSourceStoreWriter>(),
                sp.GetRequiredService<IBackgroundTaskRunner>(),
                sp.GetRequiredService<IUiThreadDispatcher>(),
                sp.GetRequiredService<IDaqManagementService>(),
                sp.GetRequiredService<IRecordedSessionReprocessor>(),
                sp.GetRequiredService<IEditorFactory>()));
        ServiceCollection.AddSingleton<IImportSessionsCoordinator>(sp =>
            sp.GetRequiredService<ImportSessionsCoordinator>());
        ServiceCollection.AddSingleton<BikeListViewModel>();
        ServiceCollection.AddSingleton<SessionListViewModel>();
        ServiceCollection.AddSingleton<LiveDaqListViewModel>();
        ServiceCollection.AddSingleton<PairedDeviceListViewModel>();
        ServiceCollection.AddSingleton<ImportSessionsViewModel>();
        ServiceCollection.AddSingleton<SetupListViewModel>();
        ServiceCollection.AddSingleton<MainPagesViewModel>();
        ServiceCollection.AddSingleton<WelcomeScreenViewModel>();
        ServiceCollection.AddSingleton<MainViewModel>();
        ServiceCollection.AddSingleton<ShellWorkspaceViewModel>();
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

        // Services with constructor-time event subscriptions are
        // eagerly resolved here so the subscriptions are wired before any
        // sync, pairing, or telemetry arrival can happen.
        _ = Services.GetRequiredService<IProcessingDependencyHashIndex>();
        _ = Services.GetRequiredService<ISessionTrackReader>();
        _ = Services.GetRequiredService<SessionSyncApplier>();
        _ = Services.GetRequiredService<IPairedDeviceCoordinator>();
        _ = Services.GetRequiredService<ISyncCoordinator>();

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

        // One-time, per-device normalization: reset every source-backed session's
        // processing option to 25 ms and recompute it so its fingerprint records the
        // option it processed. Fire-and-forget off the UI thread; the pass waits for
        // database initialization itself and is resumable via its core_migration marker.
        _ = Services.GetRequiredService<ProcessingOptionsResetMigration>().RunAsync();
        _ = Services.GetRequiredService<RecordedSessionSourceRetentionCleanup>().RunAsync();

        var fileService = Services.GetRequiredService<IFilesService>();
        var dialogHost = Services.GetRequiredService<IDialogHost>();
        var shellCoordinator = Services.GetRequiredService<IShellCoordinator>();

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                var mainWindowViewModel = Services.GetRequiredService<MainWindowViewModel>();
                var mainWindow = new MainWindow();
                desktop.MainWindow = mainWindow;
                Services.GetRequiredService<IPlotZoomState>()
                    .SetSurface(mainWindow.FindControl<PlotZoomOverlayHost>("PlotZoomOverlay"));
                fileService.SetTarget(TopLevel.GetTopLevel(mainWindow));
                dialogHost.SetOwner(mainWindow);
                dialogHost.SetOverlayHost(mainWindow);
                dialogHost.SetPresentationMode(DialogPresentationMode.Window);
                mainWindow.DataContext = mainWindowViewModel;
                desktop.Exit += (_, _) => LoggingBootstrapper.FlushAndClose();
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                var mainViewModel = Services.GetRequiredService<MainViewModel>();
                var mobileNavigationPageHost = Services.GetRequiredService<IMobileNavigationPageHost>();
                var mainView = new MainView
                {
                    DataContext = mainViewModel
                };
                mainView.SetNavigationPageHost(mobileNavigationPageHost);
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
                        var handled = mainViewModel.TryCloseTransientShellSurface();
                        if (!handled)
                        {
                            handled = shellCoordinator.GoBack();
                        }

                        e.Handled = handled;
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

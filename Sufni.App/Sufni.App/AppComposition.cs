using Sufni.App.Shared.Stores;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Linq;

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
using Sufni.App.Shell.ViewModels;
using Sufni.App.Shared.Views.Overlays;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.SyncAndPairing.ViewModels.ItemLists;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
using Sufni.App.Segments.Services.Matching;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Video.Coordinators;
using Sufni.App.Video.Services;
using Sufni.App.Video.Stores;
using Sufni.App.Video.ViewModels;
using Sufni.App.Video.Models;

namespace Sufni.App;

public static class AppComposition
{
    public static IServiceCollection AddSharedServices(this IServiceCollection services, PlatformRoot root)
    {
        services.AddSingleton(root.Paths);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<StorePublication>();
        services.AddSingleton<IUiTimerScheduler, DispatcherUiTimerScheduler>();
        services.AddSingleton<IAppEnvironment>(new AppEnvironment(
            root.LaunchPreferences.LayoutProfile, root.Capabilities, root.Input));
        root.RegisterPlatformServices(services);
        if (root.Capabilities.CanPlayVideo != services.Any(descriptor => descriptor.ServiceType == typeof(IVideoPlayerFactory)))
        {
            throw new InvalidOperationException("Video capability and player registration must agree.");
        }

        services.AddSingleton<IShellCoordinator, ShellWorkspaceCoordinator>();

        services.AddSingleton<ViewLocator>(sp => new ViewLocator(
            sp.GetRequiredService<IAppEnvironment>()));
        services.AddSingleton<IBackgroundTaskRunner, BackgroundTaskRunner>();
        services.AddSingleton<IUiThreadDispatcher, AvaloniaUiThreadDispatcher>();
        services.AddSingleton<ILayoutProfileTransitionState, LayoutProfileTransitionState>();
        services.AddSingleton<IKinematicSolutionCache, KinematicSolutionCache>();
        services.AddSingleton<IRearTravelCalibrationBuilder, RearTravelCalibrationBuilder>();
        services.AddSingleton<IBikeRearSuspensionValidator, BikeRearSuspensionValidator>();
        services.AddSingleton<ITelemetryBikeProcessingContextFactory, TelemetryBikeProcessingContextFactory>();
        services.AddSingleton<IBikeEditorService, BikeEditorService>();
        services.AddSingleton<ISessionPresentationService, SessionPresentationService>();
        services.AddSingleton<ISessionInsightsService, SessionInsightsService>();
        services.AddSingleton<IRecordedSessionAnalysisComputer, RecordedSessionAnalysisComputer>();
        services.AddSingleton<IRecordedSessionAnalysisResultStateFactory, RecordedSessionAnalysisResultStateFactory>();
        services.AddSingleton<ISessionTelemetryProcessor, SessionTelemetryProcessor>();
        services.AddSingleton<ISessionProcessedTelemetryReader, SessionProcessedTelemetryReader>();
        services.AddSingleton<IDaqManagementService, DaqManagementService>();
        services.AddSingleton<IDaqDriveProbe, DaqDriveProbe>();
        services.AddSingleton<ITelemetryDataStoreService, TelemetryDataStoreService>();
        services.AddSingleton<SqliteConnectionContext>(sp =>
            new SqliteConnectionContext(
                root.Paths.DatabasePath, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton(typeof(ISynchronizableRepository<>), typeof(SynchronizableRepository<>));
        services.AddSingleton<IPairedDeviceRepository, PairedDeviceRepository>();
        services.AddSingleton<IRecordedSessionSourceRepository, RecordedSessionSourceRepository>();
        services.AddSingleton<ITrackRepository, TrackRepository>();
        services.AddSingleton<ISessionTrackReader, SessionTrackReader>();
        services.AddSingleton<IFullTrackPointReader, FullTrackPointReader>();
        services.AddSingleton<ISessionRepository, SessionRepository>();
        services.AddSingleton<ISessionTelemetryWriter, SessionTelemetryWriter>();
        services.AddSingleton<ISessionBlobSwapRequestStore, SessionBlobSwapRequestStore>();
        services.AddSingleton<ISyncDataStore, SynchronizationMergeEngine>();
        services.AddSingleton<ISessionContentReader, SessionContentReader>();
        services.AddSingleton<ISessionOperationRunner, SessionOperationRunner>();
        services.AddSingleton<IAppPreferences>(sp => new AppPreferences(
            root.Paths.PreferencesPath, root.LaunchPreferences, sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ISessionPreferences>(sp => sp.GetRequiredService<IAppPreferences>().Session);
        services.AddSingleton<IUiPreferences>(sp => sp.GetRequiredService<IAppPreferences>().Ui);
        services.AddSingleton<ITileLayerService, TileLayerService>();
        services.AddSingleton<IMapViewModelFactory, MapViewModelFactory>();
        services.AddSingleton<FilesService>();
        services.AddSingleton<IFilesService>(sp => sp.GetRequiredService<FilesService>());
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        services.AddSingleton<IDialogHost>(sp => sp.GetRequiredService<DialogService>());
        services.AddSingleton<IPlotZoomState, PlotZoomState>();
        services.AddSingleton<BikeStore>();
        services.AddSingleton<IBikeStore>(sp => sp.GetRequiredService<BikeStore>());
        services.AddSingleton<IBikeStoreWriter>(sp => sp.GetRequiredService<BikeStore>());
        services.AddSingleton<IBikeDependencyQuery, BikeDependencyQuery>();
        services.AddSingleton<ILiveDaqKnownBoardsQuery, LiveDaqKnownBoardsQuery>();
        services.AddSingleton<Func<ImportSessionsViewModel>>(sp =>
            () => sp.GetRequiredService<ImportSessionsViewModel>());
        services.AddSingleton<IEditorFactory, EditorFactory>();
        services.AddSingleton<Func<IEditorFactory>>(sp => () => sp.GetRequiredService<IEditorFactory>());
        services.AddSingleton<BikeCoordinator>();
        services.AddSingleton<IBikeCoordinator>(sp => sp.GetRequiredService<BikeCoordinator>());
        services.AddSingleton<SetupStore>();
        services.AddSingleton<ISetupStore>(sp => sp.GetRequiredService<SetupStore>());
        services.AddSingleton<ISetupStoreWriter>(sp => sp.GetRequiredService<SetupStore>());
        services.AddSingleton<ISetupPersistenceTransactionRunner, SetupPersistenceTransactionRunner>();
        services.AddSingleton<SetupCoordinator>();
        services.AddSingleton<ISetupCoordinator>(sp => sp.GetRequiredService<SetupCoordinator>());
        services.AddSingleton<SessionStore>();
        services.AddSingleton<ISessionStore>(sp => sp.GetRequiredService<SessionStore>());
        services.AddSingleton<ISessionStoreWriter>(sp => sp.GetRequiredService<SessionStore>());
        services.AddSingleton<RecordedSessionSourceStore>();
        services.AddSingleton<IRecordedSessionSourceStore>(sp => sp.GetRequiredService<RecordedSessionSourceStore>());
        services.AddSingleton<IRecordedSessionSourceStoreWriter>(sp => sp.GetRequiredService<RecordedSessionSourceStore>());
        services.AddSingleton<ISessionVideoRepository, SessionVideoRepository>();
        services.AddSingleton<IVideoMetadataReader, Mp4VideoMetadataReader>();
        services.AddSingleton<SessionVideoStore>();
        services.AddSingleton<ISessionVideoStore>(sp => sp.GetRequiredService<SessionVideoStore>());
        services.AddSingleton<ISessionVideoStoreWriter>(sp => sp.GetRequiredService<SessionVideoStore>());
        services.AddSingleton<IVideoCoordinator, VideoCoordinator>();
        services.AddSingleton<IVideoPaneFactory, VideoPaneFactory>();
        services.AddSingleton<ISessionSegmentRepository, SessionSegmentRepository>();
        services.AddSingleton<SessionSegmentStore>();
        services.AddSingleton<ISessionSegmentStore>(sp => sp.GetRequiredService<SessionSegmentStore>());
        services.AddSingleton<ISessionSegmentStoreWriter>(sp => sp.GetRequiredService<SessionSegmentStore>());
        services.AddSingleton<ISegmentMatcher, SegmentMatcher>();
        services.AddSingleton<ISegmentCoordinator, SegmentCoordinator>();
        services.AddSingleton<IStravaSegmentService, StravaSegmentService>();
        services.AddSingleton<ISegmentPickerFactory, SegmentPickerFactory>();
        services.AddSingleton<ISegmentComparisonFactory, SegmentComparisonFactory>();
        services.AddSingleton<IAppDataRefresher, AppDataRefresher>();
        services.AddSingleton<IProcessingFingerprintService, ProcessingFingerprintService>();
        services.AddSingleton<IRecordedSessionProcessingOptionCache, RecordedSessionProcessingOptionCache>();
        services.AddSingleton<IProcessingDependencyHashIndex, ProcessingDependencyHashIndex>();
        services.AddSingleton<IRecordedSessionDomainQuery, RecordedSessionDomainQuery>();
        services.AddSingleton<IRecordedSessionProjection, RecordedSessionProjection>();
        services.AddSingleton<IRecordedSessionReprocessor, RecordedSessionReprocessor>();
        services.AddSingleton<IRecordedSessionSourceSyncQuery, RecordedSessionSourceSyncQuery>();
        services.AddSingleton<TrackCoordinator>();
        services.AddSingleton<ITrackCoordinator>(sp => sp.GetRequiredService<TrackCoordinator>());
        services.AddSingleton<SessionLoader>(sp => new SessionLoader(
            sp.GetRequiredService<ISessionStore>(),
            sp.GetRequiredService<ISessionProcessedTelemetryReader>(),
            sp.GetRequiredService<ITrackCoordinator>(),
            sp.GetRequiredService<IRecordedSessionDomainQuery>()));
        services.AddSingleton<ISessionRecomputeEngine>(sp => new SessionRecomputeEngine(
            sp.GetRequiredService<ISessionStoreWriter>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<ISessionTelemetryWriter>(),
            sp.GetRequiredService<ISynchronizableRepository<Track>>(),
            sp.GetRequiredService<IBackgroundTaskRunner>(),
            sp.GetRequiredService<IRecordedSessionProcessingOptionCache>(),
            sp.GetRequiredService<IRecordedSessionSourceStoreWriter>(),
            sp.GetRequiredService<IRecordedSessionDomainQuery>(),
            sp.GetRequiredService<IRecordedSessionReprocessor>()));
        services.AddSingleton<ISessionPersistenceTransactionRunner, SessionPersistenceTransactionRunner>();
        services.AddSingleton<ISessionEditingCoordinator, SessionEditingCoordinator>();
        services.AddSingleton<SessionCommandService>(sp => new SessionCommandService(
            sp.GetRequiredService<ISessionStoreWriter>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<ISessionTelemetryWriter>(),
            sp.GetRequiredService<ISynchronizableRepository<Setup>>(),
            sp.GetRequiredService<ISynchronizableRepository<Bike>>(),
            sp.GetRequiredService<IRecordedSessionSourceStoreWriter>(),
            sp.GetRequiredService<IRecordedSessionReprocessor>(),
            sp.GetRequiredService<IBackgroundTaskRunner>(),
            sp.GetRequiredService<ISessionPreferences>(),
            sp.GetRequiredService<ISessionRecomputeEngine>(),
            sp.GetRequiredService<Func<IEditorFactory>>(),
            sp.GetRequiredService<ISessionPersistenceTransactionRunner>()));
        if (root.Capabilities.CanHostSyncServer)
        {
            services.AddSingleton<SessionSyncApplier>(sp => new SessionSyncApplier(
                sp.GetRequiredService<ISessionStoreWriter>(),
                sp.GetRequiredService<IRecordedSessionSourceStoreWriter>(),
                sp.GetRequiredService<IUiThreadDispatcher>(),
                sp.GetRequiredService<ISynchronizationServerService>()));
        }
        services.AddSingleton<ISessionCoordinator, SessionCoordinator>();
        services.AddSingleton<ProcessingOptionsResetMigration>(sp => new ProcessingOptionsResetMigration(
            sp.GetRequiredService<SqliteConnectionContext>(),
            sp.GetRequiredService<IRecordedSessionSourceRepository>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<IAppDataRefresher>(),
            sp.GetRequiredService<ISessionPreferences>(),
            sp.GetRequiredService<ISessionRecomputeEngine>(),
            sp.GetRequiredService<IBackgroundTaskRunner>()));
        services.AddSingleton<RecordedSessionSourceRetentionCleanup>(sp => new RecordedSessionSourceRetentionCleanup(
            sp.GetRequiredService<SqliteConnectionContext>(),
            sp.GetRequiredService<IRecordedSessionSourceRepository>(),
            sp.GetRequiredService<IRecordedSessionSourceStoreWriter>(),
            sp.GetRequiredService<IBackgroundTaskRunner>()));
        services.AddSingleton<LiveDaqStore>();
        services.AddSingleton<ILiveDaqStore>(sp => sp.GetRequiredService<LiveDaqStore>());
        services.AddSingleton<ILiveDaqStoreWriter>(sp => sp.GetRequiredService<LiveDaqStore>());
        services.AddSingleton<IDaqBrowseOwner, DaqBrowseOwner>();
        services.AddSingleton<ILiveDaqBoardIdInspector, LiveDaqBoardIdInspector>();
        services.AddSingleton<LiveDaqCatalogService>();
        services.AddSingleton<ILiveDaqCatalogService>(sp =>
            sp.GetRequiredService<LiveDaqCatalogService>());
        services.AddSingleton<ILiveDaqClientFactory, LiveDaqClientFactory>();
        services.AddSingleton<ILiveDaqSharedStreamRegistry, LiveDaqSharedStreamRegistry>();
        services.AddSingleton<LiveSignalPipelineFactory>();
        services.AddSingleton<ILiveSessionServiceFactory, LiveSessionServiceFactory>();
        services.AddSingleton<LiveDaqCoordinator>();
        services.AddSingleton<ILiveDaqCoordinator>(sp => sp.GetRequiredService<LiveDaqCoordinator>());
        services.AddSingleton<PairedDeviceStore>();
        services.AddSingleton<IPairedDeviceStore>(sp => sp.GetRequiredService<PairedDeviceStore>());
        services.AddSingleton<IPairedDeviceStoreWriter>(sp => sp.GetRequiredService<PairedDeviceStore>());
        services.AddSingleton<PairedDeviceCoordinator>();
        services.AddSingleton<IPairedDeviceCoordinator>(sp => sp.GetRequiredService<PairedDeviceCoordinator>());
        services.AddSingleton<SyncCoordinator>();
        services.AddSingleton<ISyncCoordinator>(sp => sp.GetRequiredService<SyncCoordinator>());
        services.AddSingleton<ImportSessionsCoordinator>(sp =>
            new ImportSessionsCoordinator(
                sp.GetRequiredService<ISessionTelemetryWriter>(),
                sp.GetRequiredService<ISynchronizableRepository<Setup>>(),
                sp.GetRequiredService<ISynchronizableRepository<Bike>>(),
                sp.GetRequiredService<ISessionStoreWriter>(),
                sp.GetRequiredService<IRecordedSessionSourceStoreWriter>(),
                sp.GetRequiredService<IBackgroundTaskRunner>(),
                sp.GetRequiredService<IDaqManagementService>(),
                sp.GetRequiredService<IRecordedSessionReprocessor>(),
                sp.GetRequiredService<IEditorFactory>(),
                sp.GetRequiredService<IUiThreadDispatcher>()));
        services.AddSingleton<IImportSessionsCoordinator>(sp =>
            sp.GetRequiredService<ImportSessionsCoordinator>());
        services.AddSingleton<BikeListViewModel>();
        services.AddSingleton<SessionListViewModel>();
        services.AddSingleton<LiveDaqListViewModel>();
        services.AddSingleton<PairedDeviceListViewModel>();
        services.AddSingleton<ImportSessionsViewModel>();
        services.AddSingleton<SetupListViewModel>();
        services.AddSingleton<MainPagesViewModel>();
        services.AddSingleton<ShellWorkspaceViewModel>();
        services.AddSingleton<ShellRootViewModel>();

        return services;
    }
}

global using Sufni.App.Tests.Infrastructure;

using System;
using System.Threading;
using System.Threading.Tasks;
using NSubstitute;
using Sufni.App.BikeEditing;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.ExtensionHost.Services;
using Sufni.App.ExtensionHost.SessionGraph;
using Sufni.App.ExtensionHosting.Database;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.SessionGraph;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Services.Management;
using Sufni.App.SetupEditing;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Infrastructure;

internal static class TestCoordinatorSubstitutes
{
    public static BikeCoordinator Bike()
    {
        var coordinator = Substitute.For<BikeCoordinator>(
            Substitute.For<IBikeStoreWriter>(),
            Substitute.For<ISynchronizableRepository<Bike>>(),
            Substitute.For<IBikeDependencyQuery>(),
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IBikeEditorService>(),
            new Func<IEditorFactory>(() => Substitute.For<IEditorFactory>()),
            Substitute.For<IExtensionCascadeService>());

        coordinator.OpenCreateAsync().Returns(Task.CompletedTask);
        coordinator.OpenEditAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        coordinator.LoadAnalysisAsync(Arg.Any<RearSuspension?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeEditorAnalysisResult>(new BikeEditorAnalysisResult.Unavailable()));
        coordinator.LoadImageAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImageLoadResult>(new BikeImageLoadResult.Canceled()));
        coordinator.ImportBikeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeImportResult>(new BikeImportResult.Canceled()));
        coordinator.ImportLeverageRatioAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LeverageRatioImportResult>(new LeverageRatioImportResult.Canceled()));
        coordinator.ExportBikeAsync(Arg.Any<Bike>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BikeExportResult>(new BikeExportResult.Canceled()));

        return coordinator;
    }

    public static SetupCoordinator Setup()
    {
        var coordinator = Substitute.For<SetupCoordinator>(
            Substitute.For<ISetupStoreWriter>(),
            Substitute.For<IBikeStoreWriter>(),
            Substitute.For<ISynchronizableRepository<Setup>>(),
            Substitute.For<ISynchronizableRepository<Bike>>(),
            Substitute.For<ISynchronizableRepository<Board>>(),
            Substitute.For<ITelemetryDataStoreService>(),
            Substitute.For<IFilesService>(),
            Substitute.For<IBackgroundTaskRunner>(),
            Substitute.For<IShellCoordinator>(),
            new Func<IEditorFactory>(() => Substitute.For<IEditorFactory>()),
            Substitute.For<IExtensionCascadeService>());

        coordinator.OpenCreateAsync(Arg.Any<Guid?>()).Returns(Task.CompletedTask);
        coordinator.OpenCreateForDetectedBoardAsync().Returns(Task.CompletedTask);
        coordinator.OpenEditAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        coordinator.ImportSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SetupImportResult>(new SetupImportResult.Canceled()));
        coordinator.ExportSetupAsync(Arg.Any<Setup>(), Arg.Any<Bike>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SetupExportResult>(new SetupExportResult.Canceled()));

        return coordinator;
    }

    public static TrackCoordinator Track()
    {
        var coordinator = Substitute.For<TrackCoordinator>(
            Substitute.For<ITrackRepository>(),
            Substitute.For<ISynchronizableRepository<Track>>(),
            Substitute.For<ISessionRepository>(),
            Substitute.For<IFilesService>(),
            Substitute.For<IBackgroundTaskRunner>());

        coordinator.ImportGpxAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GpxImportResult(0, 0)));

        return coordinator;
    }

    public static SessionCoordinator Session()
    {
        var sessionStore = Substitute.For<ISessionStoreWriter>();
        var sessionRepository = Substitute.For<ISessionRepository>();
        var backgroundTaskRunner = Substitute.For<IBackgroundTaskRunner>();
        var trackCoordinator = Track();
        var sessionPresentationService = Substitute.For<ISessionPresentationService>();
        var domainQuery = Substitute.For<IRecordedSessionDomainQuery>();
        var shell = Substitute.For<IShellCoordinator>();
        var sessionPreferences = Substitute.For<ISessionPreferences>().WithDefaultObserveRecorded();
        var sourceStore = Substitute.For<IRecordedSessionSourceStoreWriter>();
        var sourceRepository = Substitute.For<IRecordedSessionSourceRepository>();
        var reprocessor = Substitute.For<IRecordedSessionReprocessor>();
        var trackEntityRepository = Substitute.For<ISynchronizableRepository<Track>>();
        var sessionEntityRepository = Substitute.For<ISynchronizableRepository<Session>>();
        var extensionCascadeService = Substitute.For<IExtensionCascadeService>();
        var sessionLoader = new SessionLoader(
            sessionStore,
            sessionRepository,
            Substitute.For<ISessionCacheStore>(),
            Substitute.For<IHttpApiService>(),
            backgroundTaskRunner,
            trackCoordinator,
            sessionPresentationService,
            domainQuery);
        var sessionSaver = new SessionSaver(
            sessionStore,
            sessionRepository,
            shell);
        var liveCaptureSaver = new LiveCaptureSaver(
            sessionStore,
            Substitute.For<ISynchronizableRepository<Setup>>(),
            Substitute.For<ISynchronizableRepository<Bike>>(),
            sessionRepository,
            backgroundTaskRunner,
            sessionPreferences,
            sourceStore,
            reprocessor);
        var sessionRecomputer = new SessionRecomputer(
            sessionStore,
            sessionRepository,
            trackEntityRepository,
            sessionEntityRepository,
            backgroundTaskRunner,
            sessionPreferences,
            sourceStore,
            domainQuery,
            reprocessor,
            extensionCascadeService);
        var sessionDeleter = new SessionDeleter(
            sessionStore,
            sessionRepository,
            trackEntityRepository,
            sessionEntityRepository,
            sessionPreferences,
            shell,
            sourceRepository,
            sourceStore,
            extensionCascadeService);

        var coordinator = Substitute.For<SessionCoordinator>(
            sessionStore,
            sessionLoader,
            sessionSaver,
            liveCaptureSaver,
            sessionRecomputer,
            sessionDeleter,
            shell,
            new Func<IEditorFactory>(() => Substitute.For<IEditorFactory>()));

        coordinator.OpenEditAsync(Arg.Any<Guid>()).Returns(Task.CompletedTask);
        coordinator.RecomputeAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(new SessionRecomputeResult.NotRecomputable(new SessionStaleness.MissingRawSource()));

        return coordinator;
    }

    public static LiveDaqCoordinator LiveDaq()
    {
        var coordinator = Substitute.For<LiveDaqCoordinator>(
            Substitute.For<ILiveDaqStoreWriter>(),
            Substitute.For<ILiveDaqKnownBoardsQuery>(),
            Substitute.For<ILiveDaqCatalogService>(),
            Substitute.For<ILiveDaqSharedStreamRegistry>(),
            Substitute.For<ILiveSessionServiceFactory>(),
            Substitute.For<IShellCoordinator>(),
            new Func<IEditorFactory>(() => Substitute.For<IEditorFactory>()));

        coordinator.SelectAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        coordinator.OpenSessionAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        return coordinator;
    }

    public static ImportSessionsCoordinator ImportSessions()
    {
        var coordinator = Substitute.For<ImportSessionsCoordinator>(
            Substitute.For<ISessionRepository>(),
            Substitute.For<ISynchronizableRepository<Setup>>(),
            Substitute.For<ISynchronizableRepository<Bike>>(),
            Substitute.For<ISessionStoreWriter>(),
            Substitute.For<IRecordedSessionSourceStoreWriter>(),
            Substitute.For<IShellCoordinator>(),
            Substitute.For<IBackgroundTaskRunner>(),
            new InlineUiThreadDispatcher(),
            Substitute.For<IDaqManagementService>(),
            Substitute.For<IRecordedSessionReprocessor>(),
            new Func<ImportSessionsViewModel>(() => null!));

        coordinator.OpenAsync().Returns(Task.CompletedTask);

        return coordinator;
    }

    public static SyncCoordinator Sync() =>
        new(
            Substitute.For<IBikeStoreWriter>(),
            Substitute.For<ISetupStoreWriter>(),
            Substitute.For<ISessionStoreWriter>(),
            Substitute.For<IRecordedSessionSourceStore>(),
            Substitute.For<IPairedDeviceStoreWriter>(),
            null,
            null);
}

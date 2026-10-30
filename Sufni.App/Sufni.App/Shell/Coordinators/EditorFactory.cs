using Sufni.App.Acquisition.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Shared.Base;
using System;
using System.Threading.Tasks;

using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.ViewModels;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Coordinators;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Stores;
using Sufni.App.Setups.ViewModels.Editors;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Video.ViewModels;
namespace Sufni.App.Shell.Coordinators;

internal sealed class EditorFactory(
    IBikeCoordinator bikeCoordinator,
    IBikeDependencyQuery bikeDependencyQuery,
    IBikeStore bikeStore,
    ISetupCoordinator setupCoordinator,
    ISetupStore setupStore,
    ISessionCoordinator sessionCoordinator,
    ISessionLoader sessionLoader,
    ISessionRecomputeEngine recomputeEngine,
    ITrackCoordinator trackCoordinator,
    ISessionStore sessionStore,
    IRecordedSessionProjection recordedSessionProjection,
    ISessionPresentationService sessionPresentationService,
    IRecordedSessionAnalysisResultStateFactory analysisResultStateFactory,
    IMapViewModelFactory mapViewModelFactory,
    ISessionPreferences sessionPreferences,
    ISessionProcessedTelemetryReader processedTelemetryReader,
    IDaqManagementService daqManagementService,
    IFilesService filesService,
    ILiveDaqKnownBoardsQuery liveDaqKnownBoardsQuery,
    ILiveDaqStore liveDaqStore,
    IDialogService dialogService,
    IUiThreadDispatcher uiThreadDispatcher,
    IBackgroundTaskRunner backgroundTaskRunner,
    IImportSessionsCoordinator importSessionsCoordinator,
    ITelemetryDataStoreService telemetryDataStoreService,
    ILiveDaqSharedStreamRegistry sharedStreamRegistry,
    ILiveSessionServiceFactory liveSessionServiceFactory,
    IVideoPaneFactory videoPaneFactory,
    ISessionEditingCoordinator sessionEditingCoordinator,
    ISegmentPickerFactory segmentPickerFactory,
    ISegmentComparisonFactory segmentComparisonFactory,
    ISessionOperationRunner sessionOperationRunner,
    TimeProvider timeProvider,
    IUiTimerScheduler uiTimerScheduler) : IEditorFactory
{
    public TabPageViewModelBase? CreateEditor(EditorKey key, IShellCoordinator shell) => key switch
    {
        EditorKey.NewBike => CreateBikeEditor(BikeSnapshot.From(new Bike(Guid.NewGuid(), "new bike")), true, shell),
        EditorKey.Bike bike => bikeStore.Get(bike.Id) is { } snapshot ? CreateBikeEditor(snapshot, false, shell) : null,
        EditorKey.NewSetup setup => CreateSetupEditor(setupCoordinator.CreateDraft(setup.SuggestedBoardId), true, shell),
        EditorKey.Setup setup => setupStore.Get(setup.Id) is { } snapshot ? CreateSetupEditor(snapshot, false, shell) : null,
        EditorKey.Session session => sessionStore.Get(session.Id) is { } snapshot ? CreateSessionDetail(snapshot, shell) : null,
        EditorKey.ImportSessions => new ImportSessionsViewModel(
            telemetryDataStoreService, filesService, shell, dialogService,
            importSessionsCoordinator, setupStore, uiThreadDispatcher, timeProvider),
        EditorKey.LiveDaq daq => liveDaqStore.Get(daq.IdentityKey) is { } snapshot
            ? CreateLiveDaqDetail(snapshot, sharedStreamRegistry.GetOrCreate(snapshot), shell) : null,
        EditorKey.LiveSession session => CreateLiveSession(session.IdentityKey, shell),
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    private BikeEditorViewModel CreateBikeEditor(BikeSnapshot snapshot, bool isNew, IShellCoordinator shell) =>
        new(
            snapshot,
            isNew,
            bikeCoordinator,
            bikeDependencyQuery,
            shell,
            dialogService,
            uiThreadDispatcher,
            timeProvider: timeProvider) { IsDirty = isNew };

    private SetupEditorViewModel CreateSetupEditor(SetupSnapshot snapshot, bool isNew, IShellCoordinator shell) =>
        new(
            snapshot,
            isNew,
            bikeStore,
            bikeCoordinator,
            setupCoordinator,
            shell,
            dialogService,
            uiThreadDispatcher,
            timeProvider: timeProvider) { IsDirty = isNew };

    private SessionDetailViewModel CreateSessionDetail(SessionSnapshot snapshot, IShellCoordinator shell) =>
        new(
            snapshot,
            sessionCoordinator,
            sessionLoader,
            recomputeEngine,
            trackCoordinator,
            sessionStore,
            recordedSessionProjection,
            mapViewModelFactory,
            shell,
            dialogService,
            sessionPreferences,
            uiThreadDispatcher,
            deferDomainHandlingWhenInactive: true,
            processedTelemetryReader,
            analysisResultStateFactory,
            sessionEditingCoordinator,
            segmentPickerFactory,
            segmentComparisonFactory,
            sessionOperationRunner,
            timeProvider,
            bikeCoordinator,
            videoPaneFactory);

    private LiveDaqDetailViewModel CreateLiveDaqDetail(
        LiveDaqSnapshot snapshot,
        ILiveDaqSharedStream sharedStream, IShellCoordinator shell) =>
        new(
            snapshot,
            sharedStream,
            daqManagementService,
            filesService,
            shell,
            dialogService,
            liveDaqKnownBoardsQuery,
            liveDaqStore,
            uiThreadDispatcher,
            timeProvider,
            uiTimerScheduler);

    private LiveSessionDetailViewModel? CreateLiveSession(string identityKey, IShellCoordinator shell)
    {
        if (liveDaqStore.Get(identityKey) is not { } snapshot ||
            liveDaqKnownBoardsQuery.GetSessionContext(identityKey) is not { } context) return null;

        return CreateLiveSessionDetail(context,
            liveSessionServiceFactory.Create(context, sharedStreamRegistry.GetOrCreate(snapshot)), shell);
    }

    private LiveSessionDetailViewModel CreateLiveSessionDetail(
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService, IShellCoordinator shell) =>
        new(
            context,
            liveSessionService,
            sessionCoordinator,
            sessionPresentationService,
            backgroundTaskRunner,
            mapViewModelFactory,
            shell,
            dialogService,
            uiThreadDispatcher,
            timeProvider,
            uiTimerScheduler,
            bikeCoordinator);
}

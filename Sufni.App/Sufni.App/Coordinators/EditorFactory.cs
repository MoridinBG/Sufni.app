using System.Collections.Generic;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.ExtensionHost.SessionDetails;
using Sufni.App.ExtensionHost.Services;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Services.Management;
using Sufni.App.SessionGraph;
using Sufni.App.SessionDetails;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Coordinators;

internal sealed class EditorFactory(
    IBikeCoordinator bikeCoordinator,
    IBikeDependencyQuery bikeDependencyQuery,
    IBikeStore bikeStore,
    ISetupCoordinator setupCoordinator,
    ISessionCoordinator sessionCoordinator,
    ISessionStore sessionStore,
    IRecordedSessionGraph recordedSessionGraph,
    ISessionPresentationService sessionPresentationService,
    ISessionAnalysisService sessionAnalysisService,
    ITileLayerService tileLayerService,
    ISessionPreferences sessionPreferences,
    ILiveDaqCoordinator liveDaqCoordinator,
    IDaqManagementService daqManagementService,
    IFilesService filesService,
    ILiveDaqKnownBoardsQuery liveDaqKnownBoardsQuery,
    ILiveDaqStore liveDaqStore,
    IShellCoordinator shell,
    IDialogService dialogService,
    IUiThreadDispatcher uiThreadDispatcher,
    IEnumerable<IRecordedSessionExtensionFactory> recordedSessionExtensionFactories,
    IExtensionDatabaseConnection extensionDatabase,
    IRecordedSessionDataReader recordedSessionDataReader,
    IBackgroundTaskRunner backgroundTaskRunner) : IEditorFactory
{
    public BikeEditorViewModel CreateBikeEditor(BikeSnapshot snapshot, bool isNew) =>
        new(
            snapshot,
            isNew,
            bikeCoordinator,
            bikeDependencyQuery,
            shell,
            dialogService,
            uiThreadDispatcher);

    public SetupEditorViewModel CreateSetupEditor(SetupSnapshot snapshot, bool isNew) =>
        new(
            snapshot,
            isNew,
            bikeStore,
            bikeCoordinator,
            setupCoordinator,
            shell,
            dialogService,
            uiThreadDispatcher);

    public SessionDetailViewModel CreateSessionDetail(SessionSnapshot snapshot) =>
        new(
            snapshot,
            sessionCoordinator,
            sessionStore,
            recordedSessionGraph,
            sessionPresentationService,
            sessionAnalysisService,
            tileLayerService,
            shell,
            dialogService,
            sessionPreferences,
            uiThreadDispatcher,
            bikeCoordinator,
            recordedSessionExtensionFactories,
            extensionDatabase,
            recordedSessionDataReader,
            backgroundTaskRunner);

    public LiveDaqDetailViewModel CreateLiveDaqDetail(
        LiveDaqSnapshot snapshot,
        ILiveDaqSharedStream sharedStream) =>
        new(
            snapshot,
            sharedStream,
            liveDaqCoordinator,
            daqManagementService,
            filesService,
            shell,
            dialogService,
            liveDaqKnownBoardsQuery,
            liveDaqStore,
            uiThreadDispatcher);

    public LiveSessionDetailViewModel CreateLiveSessionDetail(
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService) =>
        new(
            context,
            liveSessionService,
            sessionCoordinator,
            sessionPresentationService,
            backgroundTaskRunner,
            tileLayerService,
            shell,
            dialogService,
            uiThreadDispatcher,
            bikeCoordinator);
}

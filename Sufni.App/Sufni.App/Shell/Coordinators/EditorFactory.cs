using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Contracts.Services;

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
namespace Sufni.App.Shell.Coordinators;

internal sealed class EditorFactory(
    IBikeCoordinator bikeCoordinator,
    IBikeDependencyQuery bikeDependencyQuery,
    IBikeStore bikeStore,
    ISetupCoordinator setupCoordinator,
    ISetupStore setupStore,
    ISessionCoordinator sessionCoordinator,
    ITrackCoordinator trackCoordinator,
    ISessionStore sessionStore,
    IRecordedSessionProjection recordedSessionProjection,
    ISessionPresentationService sessionPresentationService,
    IRecordedSessionAnalysisResultStateFactory analysisResultStateFactory,
    IMapViewModelFactory mapViewModelFactory,
    ISessionPreferences sessionPreferences,
    IRecordedSessionProcessingOptionCache recordedSessionProcessingOptionCache,
    ISessionProcessedTelemetryReader processedTelemetryReader,
    IRecordedSessionDerivationWindowCache recordedSessionDerivationWindowCache,
    ILiveDaqCoordinator liveDaqCoordinator,
    IDaqManagementService daqManagementService,
    IFilesService filesService,
    ILiveDaqKnownBoardsQuery liveDaqKnownBoardsQuery,
    ILiveDaqStore liveDaqStore,
    IShellCoordinator shell,
    IDialogService dialogService,
    IUiThreadDispatcher uiThreadDispatcher,
    IAppEnvironment appEnvironment,
    ILayoutProfileTransitionState layoutProfileTransitionState,
    IEnumerable<IRecordedSessionExtensionFactory> recordedSessionExtensionFactories,
    IExtensionDatabaseConnection extensionDatabase,
    IRecordedSessionDataReader recordedSessionDataReader,
    IBackgroundTaskRunner backgroundTaskRunner,
    Func<ImportSessionsViewModel> importSessionsResolver) : IEditorFactory
{
    public void OpenNewBikeEditor(BikeSnapshot snapshot)
    {
        var editor = CreateBikeEditor(snapshot, isNew: true);
        editor.IsDirty = true;
        shell.Open(editor);
    }

    public void OpenBikeEditor(BikeSnapshot snapshot)
    {
        shell.OpenOrFocus<BikeEditorViewModel>(
            editor => editor.Id == snapshot.Id,
            () => CreateBikeEditor(snapshot, isNew: false),
            CreateBikeRestoreEntry(snapshot.Id));
    }

    public Task CloseBikeEditor(Guid bikeId)
    {
        return shell.CloseIfOpen<BikeEditorViewModel>(
            editor => editor.Id == bikeId,
            forgetRestoreHistory: true,
            restoreKey: bikeId);
    }

    public BikeEditorViewModel CreateBikeEditor(BikeSnapshot snapshot, bool isNew) =>
        new(
            snapshot,
            isNew,
            bikeCoordinator,
            bikeDependencyQuery,
            shell,
            dialogService,
            uiThreadDispatcher);

    public void OpenNewSetupEditor(SetupSnapshot snapshot)
    {
        var editor = CreateSetupEditor(snapshot, isNew: true);
        editor.IsDirty = true;
        shell.Open(editor);
    }

    public void OpenSetupEditor(SetupSnapshot snapshot)
    {
        shell.OpenOrFocus<SetupEditorViewModel>(
            editor => editor.Id == snapshot.Id,
            () => CreateSetupEditor(snapshot, isNew: false),
            CreateSetupRestoreEntry(snapshot.Id));
    }

    public Task CloseSetupEditor(Guid setupId)
    {
        return shell.CloseIfOpen<SetupEditorViewModel>(
            editor => editor.Id == setupId,
            forgetRestoreHistory: true,
            restoreKey: setupId);
    }

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

    public void OpenImportSessions()
    {
        shell.OpenOrFocus<ImportSessionsViewModel>(
            _ => true,
            importSessionsResolver);
    }

    public void OpenSessionDetail(SessionSnapshot snapshot)
    {
        shell.OpenOrFocus<SessionDetailViewModel>(
            editor => editor.Id == snapshot.Id,
            () => CreateSessionDetail(snapshot),
            CreateSessionDetailRestoreEntry(snapshot.Id));
    }

    public void OpenSessionDetailInBackground(SessionSnapshot snapshot)
    {
        shell.OpenInBackground<SessionDetailViewModel>(
            editor => editor.Id == snapshot.Id,
            () => CreateSessionDetail(snapshot),
            CreateSessionDetailRestoreEntry(snapshot.Id));
    }

    public Task CloseSessionDetail(Guid sessionId)
    {
        return shell.CloseIfOpen<SessionDetailViewModel>(
            editor => editor.Id == sessionId,
            forgetRestoreHistory: true,
            restoreKey: sessionId);
    }

    public SessionDetailViewModel CreateSessionDetail(SessionSnapshot snapshot) =>
        new(
            snapshot,
            sessionCoordinator,
            trackCoordinator,
            sessionStore,
            recordedSessionProjection,
            mapViewModelFactory,
            shell,
            dialogService,
            sessionPreferences,
            uiThreadDispatcher,
            appEnvironment.LayoutProfile == UiLayoutProfile.Workspace,
            recordedSessionProcessingOptionCache,
            processedTelemetryReader,
            analysisResultStateFactory,
            recordedSessionDerivationWindowCache,
            () => this,
            bikeCoordinator,
            new ExtensionHostDependencies(
                recordedSessionExtensionFactories.ToArray(),
                extensionDatabase,
                recordedSessionDataReader,
                backgroundTaskRunner),
            layoutProfileTransitionState);

    private ClosedTabRestoreEntry CreateBikeRestoreEntry(Guid bikeId) =>
        ClosedTabRestoreEntry.For<BikeEditorViewModel>(
            bikeId,
            () => bikeStore.Get(bikeId) is { } snapshot
                ? CreateBikeEditor(snapshot, isNew: false)
                : null);

    private ClosedTabRestoreEntry CreateSetupRestoreEntry(Guid setupId) =>
        ClosedTabRestoreEntry.For<SetupEditorViewModel>(
            setupId,
            () => setupStore.Get(setupId) is { } snapshot
                ? CreateSetupEditor(snapshot, isNew: false)
                : null);

    private ClosedTabRestoreEntry CreateSessionDetailRestoreEntry(Guid sessionId) =>
        ClosedTabRestoreEntry.For<SessionDetailViewModel>(
            sessionId,
            () => sessionStore.Get(sessionId) is { } snapshot
                ? CreateSessionDetail(snapshot)
                : null);

    public void OpenLiveDaqDetail(LiveDaqSnapshot snapshot, ILiveDaqSharedStream sharedStream)
    {
        shell.OpenOrFocus<LiveDaqDetailViewModel>(
            detail => detail.IdentityKey == snapshot.IdentityKey,
            () => CreateLiveDaqDetail(snapshot, sharedStream));
    }

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

    public void OpenLiveSessionDetail(
        string identityKey,
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService)
    {
        shell.OpenOrFocus<LiveSessionDetailViewModel>(
            detail => detail.IdentityKey == identityKey,
            () => CreateLiveSessionDetail(context, liveSessionService));
    }

    public LiveSessionDetailViewModel CreateLiveSessionDetail(
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService) =>
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
            bikeCoordinator);
}

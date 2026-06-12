using System;
using System.Linq;
using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Services.Management;
using Sufni.App.SessionGraph;
using Sufni.App.SessionDetails;
using Sufni.App.Stores;
using Sufni.App.ViewModels;
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
    IMapViewModelFactory mapViewModelFactory,
    ISessionPreferences sessionPreferences,
    ILiveDaqCoordinator liveDaqCoordinator,
    IDaqManagementService daqManagementService,
    IFilesService filesService,
    ILiveDaqKnownBoardsQuery liveDaqKnownBoardsQuery,
    ILiveDaqStore liveDaqStore,
    IShellCoordinator shell,
    IDialogService dialogService,
    IUiThreadDispatcher uiThreadDispatcher,
    ISessionLayoutStrategy sessionLayoutStrategy,
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
            () => CreateBikeEditor(snapshot, isNew: false));
    }

    public void CloseBikeEditor(Guid bikeId)
    {
        shell.CloseIfOpen<BikeEditorViewModel>(editor => editor.Id == bikeId, forgetRestoreHistory: true);
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
            () => CreateSetupEditor(snapshot, isNew: false));
    }

    public void CloseSetupEditor(Guid setupId)
    {
        shell.CloseIfOpen<SetupEditorViewModel>(editor => editor.Id == setupId, forgetRestoreHistory: true);
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
            () => CreateSessionDetail(snapshot));
    }

    public void CloseSessionDetail(Guid sessionId)
    {
        shell.CloseIfOpen<SessionDetailViewModel>(editor => editor.Id == sessionId, forgetRestoreHistory: true);
    }

    public SessionDetailViewModel CreateSessionDetail(SessionSnapshot snapshot) =>
        new(
            snapshot,
            sessionCoordinator,
            sessionStore,
            recordedSessionGraph,
            sessionPresentationService,
            sessionAnalysisService,
            mapViewModelFactory,
            shell,
            dialogService,
            sessionPreferences,
            uiThreadDispatcher,
            sessionLayoutStrategy,
            bikeCoordinator,
            new ExtensionHostDependencies(
                recordedSessionExtensionFactories.ToArray(),
                extensionDatabase,
                recordedSessionDataReader,
                backgroundTaskRunner));

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

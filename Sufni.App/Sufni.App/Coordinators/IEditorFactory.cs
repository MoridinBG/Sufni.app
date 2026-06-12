using System;
using Sufni.App.Queries;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.Stores;

namespace Sufni.App.Coordinators;

public interface IEditorFactory
{
    void OpenNewBikeEditor(BikeSnapshot snapshot);

    void OpenBikeEditor(BikeSnapshot snapshot);

    void CloseBikeEditor(Guid bikeId);

    void OpenNewSetupEditor(SetupSnapshot snapshot);

    void OpenSetupEditor(SetupSnapshot snapshot);

    void CloseSetupEditor(Guid setupId);

    void OpenSessionDetail(SessionSnapshot snapshot);

    void OpenImportSessions();

    void CloseSessionDetail(Guid sessionId);

    void OpenLiveDaqDetail(LiveDaqSnapshot snapshot, ILiveDaqSharedStream sharedStream);

    void OpenLiveSessionDetail(
        string identityKey,
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService);
}

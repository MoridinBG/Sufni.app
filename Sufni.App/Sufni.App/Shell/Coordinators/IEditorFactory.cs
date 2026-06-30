using System;

using Sufni.App.Bikes.Stores;
using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
namespace Sufni.App.Shell.Coordinators;

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

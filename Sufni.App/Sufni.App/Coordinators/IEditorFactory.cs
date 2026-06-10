using System;
using Sufni.App.Queries;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.SessionDetails;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Coordinators;

public interface IEditorFactory
{
    void OpenNewBikeEditor(BikeSnapshot snapshot);

    void OpenBikeEditor(BikeSnapshot snapshot);

    void CloseBikeEditor(Guid bikeId);

    BikeEditorViewModel CreateBikeEditor(BikeSnapshot snapshot, bool isNew);

    void OpenNewSetupEditor(SetupSnapshot snapshot);

    void OpenSetupEditor(SetupSnapshot snapshot);

    void CloseSetupEditor(Guid setupId);

    SetupEditorViewModel CreateSetupEditor(SetupSnapshot snapshot, bool isNew);

    void OpenSessionDetail(SessionSnapshot snapshot);

    void CloseSessionDetail(Guid sessionId);

    SessionDetailViewModel CreateSessionDetail(SessionSnapshot snapshot);

    void OpenLiveDaqDetail(LiveDaqSnapshot snapshot, ILiveDaqSharedStream sharedStream);

    LiveDaqDetailViewModel CreateLiveDaqDetail(
        LiveDaqSnapshot snapshot,
        ILiveDaqSharedStream sharedStream);

    void OpenLiveSessionDetail(
        string identityKey,
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService);

    LiveSessionDetailViewModel CreateLiveSessionDetail(
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService);
}

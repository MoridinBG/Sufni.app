using Sufni.App.Queries;
using Sufni.App.Services.LiveStreaming;
using Sufni.App.SessionDetails;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Coordinators;

public interface IEditorFactory
{
    BikeEditorViewModel CreateBikeEditor(BikeSnapshot snapshot, bool isNew);

    SetupEditorViewModel CreateSetupEditor(SetupSnapshot snapshot, bool isNew);

    SessionDetailViewModel CreateSessionDetail(SessionSnapshot snapshot);

    LiveDaqDetailViewModel CreateLiveDaqDetail(
        LiveDaqSnapshot snapshot,
        ILiveDaqSharedStream sharedStream);

    LiveSessionDetailViewModel CreateLiveSessionDetail(
        LiveDaqSessionContext context,
        ILiveSessionService liveSessionService);
}

using System;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

/// <summary>
/// App-side extension of the SDK host-operations contract. Recorded-session
/// collaborators (staleness reconciler, workspaces) reach the session-detail
/// owner through this gateway instead of per-collaborator delegate bundles.
/// </summary>
internal interface ISessionOperationGateway : IRecordedSessionHostOperations
{
    Guid SessionId { get; }
    bool IsDirty { get; }
    bool IsViewLoaded { get; }
    bool ShouldDeferDomainHandling();
    void UpdateExtensionHostState();
    void SetGraphPreferences(SessionGraphPreferences preferences);
    void SetAnalysisRangeBoundary(double boundarySeconds);
    void PreviewDampingSpeedCutoff(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond);
    void CancelDampingSpeedCutoffPreview();
    Task CommitDampingSpeedCutoffAsync(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond);
}

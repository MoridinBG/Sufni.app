using System;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Models;
using Sufni.App.Stores;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// App-side extension of the SDK host-operations contract. Recorded-session
/// collaborators (staleness reconciler, workspaces) reach the session-detail
/// owner through this gateway instead of per-collaborator delegate bundles.
/// </summary>
internal interface ISessionOperationGateway : IRecordedSessionHostOperations
{
    Guid SessionId { get; }
    long BaselineUpdated { get; set; }
    bool IsDirty { get; }
    bool IsViewLoaded { get; }
    bool ShouldDeferDomainHandling();
    Task ApplyPersistedSnapshotAsync(SessionSnapshot snapshot);
    Task RequestLoadAsync();
    void UpdateExtensionHostState();
    void SetGraphPreferences(SessionGraphPreferences preferences);
    void SetAnalysisRangeBoundary(double boundarySeconds);
    void PreviewDampingSpeedCutoff(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond);
    void CancelDampingSpeedCutoffPreview();
    Task CommitDampingSpeedCutoffAsync(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond);
}

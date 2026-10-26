using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Tests.TestSupport.Doubles;

internal sealed class TestSessionOperationGateway : ISessionOperationGateway
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public long BaselineUpdated { get; set; }
    public bool IsDirty { get; set; }
    public bool IsViewLoaded { get; set; } = true;
    public bool DeferDomainHandling { get; set; }
    public List<SessionSnapshot> AppliedSnapshots { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Notifications { get; } = [];
    public int LoadRequestCount { get; private set; }
    public int SessionInsightsRequestCount { get; private set; }
    public List<(SuspensionType Side, DampingSpeedCircuit Circuit, double Cutoff)> CutoffPreviews { get; } = [];
    public int CutoffPreviewCancellations { get; private set; }
    public List<(SuspensionType Side, DampingSpeedCircuit Circuit, double Cutoff)> CutoffCommits { get; } = [];

    public bool ShouldDeferDomainHandling() => DeferDomainHandling;

    public Task ApplyPersistedSnapshotAsync(SessionSnapshot snapshot)
    {
        AppliedSnapshots.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task RequestLoadAsync()
    {
        LoadRequestCount++;
        return Task.CompletedTask;
    }

    public void PreviewDampingSpeedCutoff(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond) =>
        CutoffPreviews.Add((side, circuit, cutoffMmPerSecond));

    public void CancelDampingSpeedCutoffPreview() => CutoffPreviewCancellations++;

    public Task CommitDampingSpeedCutoffAsync(SuspensionType side, DampingSpeedCircuit circuit, double cutoffMmPerSecond)
    {
        CutoffCommits.Add((side, circuit, cutoffMmPerSecond));
        return Task.CompletedTask;
    }

    public List<(double StartSeconds, double EndSeconds)> AnalysisRanges { get; } = [];
    public int AnalysisRangeClearCount { get; private set; }
    public List<double> AnalysisRangeBoundaries { get; } = [];
    public List<SignalLayoutPreferences> SignalLayoutPreferences { get; } = [];

    public void SetAnalysisRange(double startSeconds, double endSeconds) =>
        AnalysisRanges.Add((startSeconds, endSeconds));

    public void ClearAnalysisRange() => AnalysisRangeClearCount++;

    public void SetSignalLayoutPreferences(SignalLayoutPreferences preferences) => SignalLayoutPreferences.Add(preferences);

    public void SetAnalysisRangeBoundary(double boundarySeconds) => AnalysisRangeBoundaries.Add(boundarySeconds);

    public void RequestSessionInsights() => SessionInsightsRequestCount++;

    public void SetTimelineVisibleRange(double startNormalized, double endNormalized, object source)
    {
    }

    public void AddError(string message) => Errors.Add(message);

    public void AddNotification(string message) => Notifications.Add(message);
}

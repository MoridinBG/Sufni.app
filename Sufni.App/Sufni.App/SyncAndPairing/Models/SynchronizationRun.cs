using System;
using System.Text.Json.Serialization;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.SyncAndPairing.Models;

public sealed record SynchronizationRunStart(
    [property: JsonPropertyName("run_id")] Guid RunId,
    [property: JsonPropertyName("db_incarnation")] string DatabaseIncarnation,
    [property: JsonPropertyName("phases")] SynchronizationPhase[] Phases)
{
    public static SynchronizationPhase[] OrderedPhases(bool pullFirst) =>
        [pullFirst ? SynchronizationPhase.PullingRemoteChanges : SynchronizationPhase.PushingLocalChanges,
         pullFirst ? SynchronizationPhase.PushingLocalChanges : SynchronizationPhase.PullingRemoteChanges,
         SynchronizationPhase.PlanningTransfers,
         SynchronizationPhase.PushingIncompleteSessionSources, SynchronizationPhase.PullingIncompleteSessionSources,
         SynchronizationPhase.PushingIncompleteSessions, SynchronizationPhase.PullingIncompleteSessions,
         SynchronizationPhase.Verifying];
}

// Absolute item counts for one phase of the current run. Only the initiating coordinator advances them.
public sealed record SynchronizationItemProgress(
    [property: JsonPropertyName("checked")] int Checked,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("previous")] int Previous,
    [property: JsonPropertyName("run_total")] int RunTotal)
{
    public bool IsValidFor(SynchronizationPhase phase) =>
        Checked >= 0 && Checked <= Total && Previous >= 0 && (long)Previous + Total <= RunTotal &&
        phase switch
        {
            SynchronizationPhase.Verifying => Total == 0 && Previous == RunTotal,
            SynchronizationPhase.PushingIncompleteSessionSources or SynchronizationPhase.PullingIncompleteSessionSources or
            SynchronizationPhase.PushingIncompleteSessions or SynchronizationPhase.PullingIncompleteSessions => Total > 0,
            _ => false
        };
}

public sealed record SynchronizationRunStarted(
    [property: JsonPropertyName("db_incarnation")] string DatabaseIncarnation);

/// <summary>The latest peer run observed by the hub.</summary>
public closed record PeerSyncActivity
{
    private PeerSyncActivity() { }
    public sealed record Idle : PeerSyncActivity;
    public sealed record Running(string DeviceId, Guid RunId, SynchronizationProgressSnapshot Progress,
        DateTimeOffset LastRequest) : PeerSyncActivity;
    public sealed record Completed(Guid RunId) : PeerSyncActivity;
    public sealed record Interrupted(Guid RunId) : PeerSyncActivity;
}

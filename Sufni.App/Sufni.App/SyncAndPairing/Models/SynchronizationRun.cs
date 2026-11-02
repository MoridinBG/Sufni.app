using System;
using System.Text.Json.Serialization;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.SyncAndPairing.Models;

public sealed record SynchronizationRunStart(
    [property: JsonPropertyName("run_id")] Guid RunId,
    [property: JsonPropertyName("db_incarnation")] string DatabaseIncarnation,
    [property: JsonPropertyName("phases")] SynchronizationPhase[] Phases);

public sealed record SynchronizationRunStarted(
    [property: JsonPropertyName("db_incarnation")] string DatabaseIncarnation);

/// <summary>The latest peer run observed by the hub.</summary>
public abstract record PeerSyncActivity
{
    private PeerSyncActivity() { }
    public sealed record Idle : PeerSyncActivity;
    public sealed record Running(string DeviceId, Guid RunId, SynchronizationPhase Phase,
        int Step, int PhaseCount, DateTimeOffset LastRequest) : PeerSyncActivity;
    public sealed record Completed(Guid RunId) : PeerSyncActivity;
    public sealed record Interrupted(Guid RunId) : PeerSyncActivity;
}

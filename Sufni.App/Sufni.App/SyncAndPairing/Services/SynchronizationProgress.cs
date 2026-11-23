using System;
using System.Collections.Generic;
using Sufni.App.Shared.Models;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.SyncAndPairing.Services;

public enum SynchronizationPhase
{
    ResolvingServer,
    PushingLocalChanges,
    PullingRemoteChanges,
    PushingIncompleteSessions,
    PullingIncompleteSessions,
    PushingIncompleteSessionSources,
    PullingIncompleteSessionSources,
    PlanningTransfers,
    Verifying,
}

public sealed record SynchronizationProgressSnapshot(
    SynchronizationPhase Phase, double Fraction, SynchronizationItemProgress? Items = null)
{
    public static SynchronizationProgressSnapshot Discovering { get; } = new(SynchronizationPhase.ResolvingServer, 0);
    public static SynchronizationProgressSnapshot Verified { get; } = new(SynchronizationPhase.Verifying, 1);

    public static SynchronizationProgressSnapshot ForPhase(SynchronizationPhase[] phases, SynchronizationPhase phase,
        SynchronizationItemProgress? items = null)
    {
        var index = Array.IndexOf(phases, phase);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(phase));
        // Discovery precedes the phases announced to the peer.
        var withinPhase = items is { Total: > 0 } ? (double)items.Checked / items.Total : 0;
        return new(phase, (1 + index + withinPhase) / (1 + phases.Length), items);
    }
}

public enum SyncActivity { Idle, Pairing, Outbound, Inbound, Stopped }

public closed record SynchronizationRunResult
{
    private SynchronizationRunResult() { }

    public sealed record Failed(SyncFailureReason Reason, string? Details = null) : SynchronizationRunResult;

    public sealed record Completed : SynchronizationRunResult;

    public sealed record IncompleteData(
        int MissingProcessedSessionCount,
        int IncompleteRecordedSourceCount,
        IReadOnlyList<SynchronizationPendingTransfer> PendingTransfers) : SynchronizationRunResult;
}

public enum SynchronizationTransferKind { ProcessedSession, RecordedSource }

public sealed record SynchronizationPendingTransfer(
    Guid Id, SynchronizationTransferKind Kind, SynchronizationDirection Direction);

using System;
using System.Collections.Generic;
using Sufni.App.Shared.Models;

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

}

public sealed record SynchronizationProgressSnapshot(
    SynchronizationPhase Phase,
    string Message,
    int CurrentStep,
    int TotalSteps,
    bool IsDeterminate)
{
    public double Fraction =>
        IsDeterminate && TotalSteps > 0
            ? Math.Clamp((double)CurrentStep / TotalSteps, 0, 1)
            : 0;
}

public enum SyncActivity { Idle, Outbound, Inbound }

public abstract record SynchronizationRunResult
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

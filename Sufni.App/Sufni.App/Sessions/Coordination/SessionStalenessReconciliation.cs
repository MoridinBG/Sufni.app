using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Shared.Models;

namespace Sufni.App.Sessions.Coordination;

public enum SessionRecomputeChoice { Inspect, ThisSession, AllSessions }

public abstract record SessionStalenessReconciliation
{
    private SessionStalenessReconciliation() { }
    public sealed record Current : SessionStalenessReconciliation;
    public sealed record Active : SessionStalenessReconciliation;
    public sealed record Required(ProcessingFingerprint? CurrentFingerprint, string? StoredFingerprint) : SessionStalenessReconciliation;
    public sealed record Unavailable(SessionStaleness Reason) : SessionStalenessReconciliation;
    public sealed record Recomputed(SessionRecomputeResult Result) : SessionStalenessReconciliation;
    public sealed record Bulk(SessionRecomputeAllOutcome Outcome) : SessionStalenessReconciliation;
}

public abstract record SessionRecomputeAllOutcome
{
    private SessionRecomputeAllOutcome() { }
    public sealed record Completed(SessionRecomputeAllResult Result) : SessionRecomputeAllOutcome;
    public sealed record Failed(SessionRecomputeFailureReason Reason, string? Details) : SessionRecomputeAllOutcome;
}

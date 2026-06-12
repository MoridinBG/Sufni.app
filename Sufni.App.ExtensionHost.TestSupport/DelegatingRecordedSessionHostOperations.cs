using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.ExtensionHost.TestSupport;

public delegate bool TryResolveTimelineAlignmentHandler(
    RecordedSessionTimelineAlignmentTarget target,
    double seconds,
    string? subjectId,
    out RecordedSessionTimelineAlignmentResolution? resolution);

/// <summary>
/// Delegate-backed <see cref="IRecordedSessionHostOperations"/> for tests;
/// unsupplied operations default to no-ops (and a substitute lease).
/// </summary>
public sealed class DelegatingRecordedSessionHostOperations(
    Action<double, double>? setAnalysisRange = null,
    Action? clearAnalysisRange = null,
    Action<double, double, object>? setTimelineVisibleRange = null,
    Action<string>? addError = null,
    Action<string>? addNotification = null,
    Func<string, IRecordedSessionOperationLease>? startOperation = null,
    Action<string>? requestPageSelection = null,
    Func<RecordedSessionTimelineAlignmentTarget, double, string?, bool>? tryBeginTimelineAlignment = null,
    TryResolveTimelineAlignmentHandler? tryResolveTimelineAlignment = null,
    Func<RecordedSessionTimelineAlignmentTarget, string?, bool>? tryCancelTimelineAlignment = null) : IRecordedSessionHostOperations
{
    public void SetAnalysisRange(double startSeconds, double endSeconds) =>
        (setAnalysisRange ?? ((_, _) => { }))(startSeconds, endSeconds);

    public void ClearAnalysisRange() => (clearAnalysisRange ?? (() => { }))();

    public void SetTimelineVisibleRange(double startNormalized, double endNormalized, object source) =>
        (setTimelineVisibleRange ?? ((_, _, _) => { }))(startNormalized, endNormalized, source);

    public bool TryBeginTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        double seconds,
        string? subjectId = null) =>
        tryBeginTimelineAlignment?.Invoke(target, seconds, subjectId) ?? false;

    public bool TryResolveTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        double seconds,
        string? subjectId,
        out RecordedSessionTimelineAlignmentResolution? resolution)
    {
        if (tryResolveTimelineAlignment is not null)
        {
            return tryResolveTimelineAlignment(target, seconds, subjectId, out resolution);
        }

        resolution = null;
        return false;
    }

    public bool TryCancelTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        string? subjectId = null) =>
        tryCancelTimelineAlignment?.Invoke(target, subjectId) ?? false;

    public void AddError(string message) => (addError ?? (_ => { }))(message);

    public void AddNotification(string message) => (addNotification ?? (_ => { }))(message);

    public IRecordedSessionOperationLease StartOperation(string description) =>
        startOperation is null
            ? Substitute.For<IRecordedSessionOperationLease>()
            : startOperation(description);

    public void RequestPageSelection(string contributionId) =>
        (requestPageSelection ?? (_ => { }))(contributionId);
}

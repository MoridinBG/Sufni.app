using NSubstitute;
using Sufni.App.ExtensionHost.RecordedSessions;

namespace Sufni.App.ExtensionHost.TestSupport;

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
    Action<string>? requestPageSelection = null) : IRecordedSessionHostOperations
{
    public void SetAnalysisRange(double startSeconds, double endSeconds) =>
        (setAnalysisRange ?? ((_, _) => { }))(startSeconds, endSeconds);

    public void ClearAnalysisRange() => (clearAnalysisRange ?? (() => { }))();

    public void SetTimelineVisibleRange(double startNormalized, double endNormalized, object source) =>
        (setTimelineVisibleRange ?? ((_, _, _) => { }))(startNormalized, endNormalized, source);

    public void AddError(string message) => (addError ?? (_ => { }))(message);

    public void AddNotification(string message) => (addNotification ?? (_ => { }))(message);

    public IRecordedSessionOperationLease StartOperation(string description) =>
        startOperation is null
            ? Substitute.For<IRecordedSessionOperationLease>()
            : startOperation(description);

    public void RequestPageSelection(string contributionId) =>
        (requestPageSelection ?? (_ => { }))(contributionId);
}

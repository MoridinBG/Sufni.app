using System;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.ExtensionHost.TestSupport.Doubles;

/// <summary>
/// Delegate-backed <see cref="IRecordedSessionHostOperations"/> for tests;
/// unsupplied operations default to no-ops.
/// </summary>
public sealed class DelegatingRecordedSessionHostOperations(
    Action<double, double>? setAnalysisRange = null,
    Action? clearAnalysisRange = null,
    Action<double, double, object>? setTimelineVisibleRange = null,
    Action<string>? addError = null,
    Action<string>? addNotification = null) : IRecordedSessionHostOperations
{
    public void SetAnalysisRange(double startSeconds, double endSeconds) =>
        (setAnalysisRange ?? ((_, _) => { }))(startSeconds, endSeconds);

    public void ClearAnalysisRange() => (clearAnalysisRange ?? (() => { }))();

    public void SetTimelineVisibleRange(double startNormalized, double endNormalized, object source) =>
        (setTimelineVisibleRange ?? ((_, _, _) => { }))(startNormalized, endNormalized, source);

    public void AddError(string message) => (addError ?? (_ => { }))(message);

    public void AddNotification(string message) => (addNotification ?? (_ => { }))(message);
}

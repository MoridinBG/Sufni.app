using System;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.Services;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public sealed record RecordedSessionHostServices
{
    public RecordedSessionHostServices(
        IObservable<RecordedSessionHostState> stateChanged,
        IExtensionDatabaseConnection database,
        IRecordedSessionDataReader dataReader,
        IBackgroundTaskRunner backgroundTaskRunner,
        IUiThreadDispatcher uiThreadDispatcher)
    {
        ArgumentNullException.ThrowIfNull(stateChanged);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(dataReader);
        ArgumentNullException.ThrowIfNull(backgroundTaskRunner);
        ArgumentNullException.ThrowIfNull(uiThreadDispatcher);

        StateChanged = stateChanged;
        Database = database;
        DataReader = dataReader;
        BackgroundTaskRunner = backgroundTaskRunner;
        UiThreadDispatcher = uiThreadDispatcher;
    }

    public IObservable<RecordedSessionHostState> StateChanged { get; }
    public IExtensionDatabaseConnection Database { get; }
    public IRecordedSessionDataReader DataReader { get; }
    public IBackgroundTaskRunner BackgroundTaskRunner { get; }
    public IUiThreadDispatcher UiThreadDispatcher { get; }
}

/// <summary>
/// Operations a recorded-session extension scope may invoke on its host.
/// The analysis range is expressed in seconds; the timeline viewport is
/// expressed in normalized fractions of the session duration.
/// </summary>
public interface IRecordedSessionHostOperations
{
    void SetAnalysisRange(double startSeconds, double endSeconds);
    void ClearAnalysisRange();
    void SetTimelineVisibleRange(double startNormalized, double endNormalized, object source);
    bool TryBeginTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        double seconds,
        string? subjectId = null);
    bool TryResolveTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        double seconds,
        string? subjectId,
        out RecordedSessionTimelineAlignmentResolution? resolution);
    bool TryCancelTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        string? subjectId = null);
    void AddError(string message);
    void AddNotification(string message);
    IRecordedSessionOperationLease StartOperation(string description);
    void RequestPageSelection(string contributionId);
    Task<Guid?> CreateDerivedSessionAsync(
        Guid fromSessionId,
        string name,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken = default);
    Task<bool> UpdateSessionOriginAsync(
        Guid sessionId,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken = default);
    Task<bool> RenameSessionAsync(
        Guid sessionId,
        string name,
        CancellationToken cancellationToken = default);
    Task<bool> RequestRecomputeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
    Task OpenSessionInBackgroundAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

public sealed class RecordedSessionHostContext
{
    public RecordedSessionHostContext(
        Guid sessionId,
        RecordedSessionHostServices services,
        IRecordedSessionHostOperations operations)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(operations);

        SessionId = sessionId;
        Services = services;
        Operations = operations;
    }

    public Guid SessionId { get; }
    public RecordedSessionHostServices Services { get; }
    public IRecordedSessionHostOperations Operations { get; }
    public IObservable<RecordedSessionHostState> StateChanged => Services.StateChanged;
    public IExtensionDatabaseConnection Database => Services.Database;
    public IRecordedSessionDataReader DataReader => Services.DataReader;
    public IBackgroundTaskRunner BackgroundTaskRunner => Services.BackgroundTaskRunner;
    public IUiThreadDispatcher UiThreadDispatcher => Services.UiThreadDispatcher;

    public void SetAnalysisRange(double startSeconds, double endSeconds)
    {
        Operations.SetAnalysisRange(startSeconds, endSeconds);
    }

    public void ClearAnalysisRange()
    {
        Operations.ClearAnalysisRange();
    }

    public void SetTimelineVisibleRange(double startNormalized, double endNormalized, object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Operations.SetTimelineVisibleRange(startNormalized, endNormalized, source);
    }

    public bool TryBeginTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        double seconds,
        string? subjectId = null)
    {
        return Operations.TryBeginTimelineAlignment(target, seconds, subjectId);
    }

    public bool TryResolveTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        double seconds,
        out RecordedSessionTimelineAlignmentResolution? resolution,
        string? subjectId = null)
    {
        return Operations.TryResolveTimelineAlignment(target, seconds, subjectId, out resolution);
    }

    public bool TryCancelTimelineAlignment(
        RecordedSessionTimelineAlignmentTarget target,
        string? subjectId = null)
    {
        return Operations.TryCancelTimelineAlignment(target, subjectId);
    }

    public void AddError(string message)
    {
        Operations.AddError(message);
    }

    public void AddNotification(string message)
    {
        Operations.AddNotification(message);
    }

    public IRecordedSessionOperationLease StartOperation(string message)
    {
        return Operations.StartOperation(message);
    }

    public void RequestPageSelection(string contributionId)
    {
        Operations.RequestPageSelection(contributionId);
    }

    public Task<Guid?> CreateDerivedSessionAsync(
        Guid fromSessionId,
        string name,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken = default)
    {
        return Operations.CreateDerivedSessionAsync(fromSessionId, name, sourceAbsoluteStartSeconds, cancellationToken);
    }

    public Task<bool> UpdateSessionOriginAsync(
        Guid sessionId,
        double sourceAbsoluteStartSeconds,
        CancellationToken cancellationToken = default)
    {
        return Operations.UpdateSessionOriginAsync(sessionId, sourceAbsoluteStartSeconds, cancellationToken);
    }

    public Task<bool> RenameSessionAsync(
        Guid sessionId,
        string name,
        CancellationToken cancellationToken = default)
    {
        return Operations.RenameSessionAsync(sessionId, name, cancellationToken);
    }

    public Task<bool> RequestRecomputeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return Operations.RequestRecomputeAsync(sessionId, cancellationToken);
    }

    public Task OpenSessionInBackgroundAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return Operations.OpenSessionInBackgroundAsync(sessionId, cancellationToken);
    }
}

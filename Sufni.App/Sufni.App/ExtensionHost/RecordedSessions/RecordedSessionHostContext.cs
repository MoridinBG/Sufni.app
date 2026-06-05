using System;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.Services;

namespace Sufni.App.ExtensionHost.RecordedSessions;

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

public sealed record RecordedSessionHostOperations
{
    public RecordedSessionHostOperations(
        Action<double, double> setAnalysisRange,
        Action clearAnalysisRange,
        Action<double, double, object> setTimelineVisibleRange,
        Action<string> addError,
        Action<string> addNotification,
        Func<string, IRecordedSessionOperationLease> startOperation,
        Action<string> requestPageSelection)
    {
        ArgumentNullException.ThrowIfNull(setAnalysisRange);
        ArgumentNullException.ThrowIfNull(clearAnalysisRange);
        ArgumentNullException.ThrowIfNull(setTimelineVisibleRange);
        ArgumentNullException.ThrowIfNull(addError);
        ArgumentNullException.ThrowIfNull(addNotification);
        ArgumentNullException.ThrowIfNull(startOperation);
        ArgumentNullException.ThrowIfNull(requestPageSelection);

        SetAnalysisRange = setAnalysisRange;
        ClearAnalysisRange = clearAnalysisRange;
        SetTimelineVisibleRange = setTimelineVisibleRange;
        AddError = addError;
        AddNotification = addNotification;
        StartOperation = startOperation;
        RequestPageSelection = requestPageSelection;
    }

    public Action<double, double> SetAnalysisRange { get; }
    public Action ClearAnalysisRange { get; }
    public Action<double, double, object> SetTimelineVisibleRange { get; }
    public Action<string> AddError { get; }
    public Action<string> AddNotification { get; }
    public Func<string, IRecordedSessionOperationLease> StartOperation { get; }
    public Action<string> RequestPageSelection { get; }
}

public sealed class RecordedSessionHostContext
{
    public RecordedSessionHostContext(
        Guid sessionId,
        RecordedSessionHostServices services,
        RecordedSessionHostOperations operations)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(operations);

        SessionId = sessionId;
        Services = services;
        Operations = operations;
    }

    public Guid SessionId { get; }
    public RecordedSessionHostServices Services { get; }
    public RecordedSessionHostOperations Operations { get; }
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
}

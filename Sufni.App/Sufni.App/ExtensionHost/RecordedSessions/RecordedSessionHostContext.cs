using System;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.Services;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public sealed class RecordedSessionHostContext
{
    private readonly Action<double, double> setAnalysisRange;
    private readonly Action clearAnalysisRange;
    private readonly Action<double, double, object> setTimelineVisibleRange;
    private readonly Action<string> addError;
    private readonly Action<string> addNotification;
    private readonly Func<string, IRecordedSessionOperationLease> startOperation;
    private readonly Action<string> requestPageSelection;

    public RecordedSessionHostContext(
        Guid sessionId,
        IObservable<RecordedSessionHostState> stateChanged,
        IExtensionDatabaseConnection database,
        IDatabaseService databaseService,
        IBackgroundTaskRunner backgroundTaskRunner,
        IUiThreadDispatcher uiThreadDispatcher,
        Action<double, double> setAnalysisRange,
        Action clearAnalysisRange,
        Action<double, double, object> setTimelineVisibleRange,
        Action<string> addError,
        Action<string> addNotification,
        Func<string, IRecordedSessionOperationLease> startOperation,
        Action<string> requestPageSelection)
    {
        ArgumentNullException.ThrowIfNull(stateChanged);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(databaseService);
        ArgumentNullException.ThrowIfNull(backgroundTaskRunner);
        ArgumentNullException.ThrowIfNull(uiThreadDispatcher);
        ArgumentNullException.ThrowIfNull(setAnalysisRange);
        ArgumentNullException.ThrowIfNull(clearAnalysisRange);
        ArgumentNullException.ThrowIfNull(setTimelineVisibleRange);
        ArgumentNullException.ThrowIfNull(addError);
        ArgumentNullException.ThrowIfNull(addNotification);
        ArgumentNullException.ThrowIfNull(startOperation);
        ArgumentNullException.ThrowIfNull(requestPageSelection);

        SessionId = sessionId;
        StateChanged = stateChanged;
        Database = database;
        DatabaseService = databaseService;
        BackgroundTaskRunner = backgroundTaskRunner;
        UiThreadDispatcher = uiThreadDispatcher;
        this.setAnalysisRange = setAnalysisRange;
        this.clearAnalysisRange = clearAnalysisRange;
        this.setTimelineVisibleRange = setTimelineVisibleRange;
        this.addError = addError;
        this.addNotification = addNotification;
        this.startOperation = startOperation;
        this.requestPageSelection = requestPageSelection;
    }

    public Guid SessionId { get; }
    public IObservable<RecordedSessionHostState> StateChanged { get; }
    public IExtensionDatabaseConnection Database { get; }
    public IDatabaseService DatabaseService { get; }
    public IBackgroundTaskRunner BackgroundTaskRunner { get; }
    public IUiThreadDispatcher UiThreadDispatcher { get; }

    public void SetAnalysisRange(double startSeconds, double endSeconds)
    {
        setAnalysisRange(startSeconds, endSeconds);
    }

    public void ClearAnalysisRange()
    {
        clearAnalysisRange();
    }

    public void SetTimelineVisibleRange(double startNormalized, double endNormalized, object source)
    {
        ArgumentNullException.ThrowIfNull(source);
        setTimelineVisibleRange(startNormalized, endNormalized, source);
    }

    public void AddError(string message)
    {
        addError(message);
    }

    public void AddNotification(string message)
    {
        addNotification(message);
    }

    public IRecordedSessionOperationLease StartOperation(string message)
    {
        return startOperation(message);
    }

    public void RequestPageSelection(string contributionId)
    {
        requestPageSelection(contributionId);
    }
}

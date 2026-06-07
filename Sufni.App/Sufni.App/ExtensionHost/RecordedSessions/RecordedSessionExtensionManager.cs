using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.RecordedSessions;

internal sealed class RecordedSessionExtensionManager : IAsyncDisposable
{
    private readonly Guid sessionId;
    private readonly IReadOnlyList<IRecordedSessionExtensionFactory> factories;
    private readonly IExtensionDatabaseConnection database;
    private readonly IRecordedSessionDataReader dataReader;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly RecordedSessionOperationCoordinator operationCoordinator;
    private readonly Action<double, double> setAnalysisRange;
    private readonly Action clearAnalysisRange;
    private readonly Action<double, double, object> setTimelineVisibleRange;
    private readonly Action<string> addError;
    private readonly Action<string> addNotification;
    private readonly Action<string> requestPageSelection;
    private readonly BehaviorSubject<RecordedSessionHostState> stateChanged;
    private readonly RecordedSessionExtensionSlotPublisher extensionSlotPublisher;
    private readonly List<IRecordedSessionExtensionScope> scopes = [];
    private readonly List<IDisposable> slotSubscriptions = [];
    private bool disposed;
    private bool extensionSlotsRebuildQueued;

    public RecordedSessionExtensionManager(
        Guid sessionId,
        IEnumerable<IRecordedSessionExtensionFactory> factories,
        IExtensionDatabaseConnection database,
        IRecordedSessionDataReader dataReader,
        IBackgroundTaskRunner backgroundTaskRunner,
        IUiThreadDispatcher uiThreadDispatcher,
        RecordedSessionOperationCoordinator operationCoordinator,
        Action<double, double> setAnalysisRange,
        Action clearAnalysisRange,
        Action<double, double, object> setTimelineVisibleRange,
        Action<string> addError,
        Action<string> addNotification,
        Action<string> requestPageSelection)
    {
        ArgumentNullException.ThrowIfNull(factories);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(dataReader);
        ArgumentNullException.ThrowIfNull(backgroundTaskRunner);
        ArgumentNullException.ThrowIfNull(uiThreadDispatcher);
        ArgumentNullException.ThrowIfNull(operationCoordinator);
        ArgumentNullException.ThrowIfNull(setAnalysisRange);
        ArgumentNullException.ThrowIfNull(clearAnalysisRange);
        ArgumentNullException.ThrowIfNull(setTimelineVisibleRange);
        ArgumentNullException.ThrowIfNull(addError);
        ArgumentNullException.ThrowIfNull(addNotification);
        ArgumentNullException.ThrowIfNull(requestPageSelection);

        this.sessionId = sessionId;
        this.factories = factories.ToArray();
        this.database = database;
        this.dataReader = dataReader;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.uiThreadDispatcher = uiThreadDispatcher;
        this.operationCoordinator = operationCoordinator;
        this.setAnalysisRange = setAnalysisRange;
        this.clearAnalysisRange = clearAnalysisRange;
        this.setTimelineVisibleRange = setTimelineVisibleRange;
        this.addError = addError;
        this.addNotification = addNotification;
        this.requestPageSelection = requestPageSelection;
        extensionSlotPublisher = new RecordedSessionExtensionSlotPublisher(ExtensionSlots, uiThreadDispatcher);
        CurrentState = new RecordedSessionHostState(
            new RecordedSessionIdentityState(sessionId, null, null, null, IsLoaded: false, IsActive: false),
            new RecordedSessionSelectionState(null),
            new RecordedSessionTimelineState(null, null, null),
            new RecordedSessionStatisticsState(
                SessionDamperPercentages.Empty,
                DampingSpeedCutoffs.Default,
                VelocityAverageMode.SampleAveraged,
                TravelHistogramMode.ActiveSuspension));
        stateChanged = new BehaviorSubject<RecordedSessionHostState>(CurrentState);
    }

    public RecordedSessionHostState CurrentState { get; private set; }
    public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();

    public async ValueTask InitializeAsync(
        RecordedSessionHostState initialState,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        UpdateHostState(initialState);
        if (scopes.Count > 0)
        {
            return;
        }

        foreach (var factory in factories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = CreateContext();
            var scope = factory.Create(context);
            scopes.Add(scope);
            AttachScopeSlots(scope.Slots);
            await scope.InitializeAsync(cancellationToken);
            scope.UpdateHostState(CurrentState);
        }

        RebuildExtensionSlotsImmediately();
    }

    public void UpdateHostState(RecordedSessionHostState state)
    {
        ThrowIfDisposed();
        CurrentState = state;
        stateChanged.OnNext(state);

        foreach (var scope in scopes)
        {
            scope.UpdateHostState(state);
        }
    }

    public async ValueTask DisposeScopesAsync()
    {
        if (disposed)
        {
            return;
        }

        operationCoordinator.CancelCurrent();
        foreach (var subscription in slotSubscriptions)
        {
            subscription.Dispose();
        }

        slotSubscriptions.Clear();
        extensionSlotsRebuildQueued = false;
        ClearExtensionSlots();
        for (var i = scopes.Count - 1; i >= 0; i--)
        {
            await scopes[i].DisposeAsync();
        }

        scopes.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await DisposeScopesAsync();
        await operationCoordinator.DisposeAsync();
        stateChanged.Dispose();
        disposed = true;
    }

    private RecordedSessionHostContext CreateContext()
    {
        return new RecordedSessionHostContext(
            sessionId,
            new RecordedSessionHostServices(
                stateChanged.AsObservable(),
                database,
                dataReader,
                backgroundTaskRunner,
                uiThreadDispatcher),
            new RecordedSessionHostOperations(
                setAnalysisRange,
                clearAnalysisRange,
                setTimelineVisibleRange,
                addError,
                addNotification,
                operationCoordinator.StartOperation,
                requestPageSelection));
    }

    private void AttachScopeSlots(RecordedSessionExtensionSlots slots)
    {
        slotSubscriptions.Add(slots.SubscribeToChanges(QueueExtensionSlotsRebuild));
    }

    private void QueueExtensionSlotsRebuild()
    {
        if (disposed || extensionSlotsRebuildQueued)
        {
            return;
        }

        extensionSlotsRebuildQueued = true;
        uiThreadDispatcher.Post(() =>
        {
            if (disposed || !extensionSlotsRebuildQueued)
            {
                return;
            }

            extensionSlotsRebuildQueued = false;
            RebuildExtensionSlots();
        });
    }

    private void RebuildExtensionSlotsImmediately()
    {
        extensionSlotsRebuildQueued = false;
        RebuildExtensionSlots();
    }

    private void RebuildExtensionSlots()
    {
        extensionSlotPublisher.Publish(builder =>
        {
            foreach (var scope in scopes)
            {
                builder.AddFrom(scope.Slots);
            }
        });
    }

    private void ClearExtensionSlots()
    {
        extensionSlotPublisher.Clear();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }
}

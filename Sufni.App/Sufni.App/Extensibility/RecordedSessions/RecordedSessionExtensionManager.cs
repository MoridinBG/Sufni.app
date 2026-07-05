using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.Extensibility.Capabilities;
namespace Sufni.App.Extensibility.RecordedSessions;

internal sealed class RecordedSessionExtensionManager : IAsyncDisposable
{
    private readonly Guid sessionId;
    private readonly IReadOnlyList<IRecordedSessionExtensionFactory> factories;
    private readonly IExtensionDatabaseConnection database;
    private readonly IRecordedSessionDataReader dataReader;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly RecordedSessionOperationCoordinator operationCoordinator;
    private readonly IRecordedSessionHostOperations operations;
    private readonly BehaviorSubject<RecordedSessionHostState> stateChanged;
    private readonly RecordedSessionExtensionSlotPublisher extensionSlotPublisher;
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly object scopesGate = new();
    private readonly List<RecordedSessionExtensionScopeRegistration> scopes = [];
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
        IRecordedSessionHostOperations operations)
    {
        ArgumentNullException.ThrowIfNull(factories);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(dataReader);
        ArgumentNullException.ThrowIfNull(backgroundTaskRunner);
        ArgumentNullException.ThrowIfNull(uiThreadDispatcher);
        ArgumentNullException.ThrowIfNull(operationCoordinator);
        ArgumentNullException.ThrowIfNull(operations);

        this.sessionId = sessionId;
        this.factories = factories.ToArray();
        ExtensionContributionValidator.ValidateRecordedSessionExtensionFactories(this.factories);
        this.database = database;
        this.dataReader = dataReader;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.uiThreadDispatcher = uiThreadDispatcher;
        this.operationCoordinator = operationCoordinator;
        this.operations = operations;
        extensionSlotPublisher = new RecordedSessionExtensionSlotPublisher(ExtensionSlots, uiThreadDispatcher);
        CurrentState = new RecordedSessionHostState(
            new RecordedSessionIdentityState(sessionId, null, null, null, IsLoaded: false, IsActive: false),
            new RecordedSessionSelectionState(null),
            new RecordedSessionTimelineState(null, null, null),
            new RecordedSessionAnalysisState(
                SessionDampingPercentages.Empty,
                DampingSpeedCutoffs.Default,
                VelocityAverageMode.SampleAveraged,
                TravelDistributionMode.ActiveSuspension));
        stateChanged = new BehaviorSubject<RecordedSessionHostState>(CurrentState);
    }

    public RecordedSessionHostState CurrentState { get; private set; }
    public RecordedSessionExtensionSlots ExtensionSlots { get; } = new();

    public async ValueTask InitializeAsync(
        RecordedSessionHostState initialState,
        CancellationToken cancellationToken = default)
    {
        await lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            UpdateHostStateCore(initialState);
            lock (scopesGate)
            {
                if (scopes.Count > 0)
                {
                    return;
                }
            }

            try
            {
                foreach (var factory in factories)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var context = CreateContext();
                    var scope = factory.Create(context);
                    ArgumentNullException.ThrowIfNull(scope);
                    lock (scopesGate)
                    {
                        scopes.Add(new RecordedSessionExtensionScopeRegistration(factory.ExtensionId, scope));
                        AttachScopeSlots(scope.Slots);
                    }

                    await scope.InitializeAsync(cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    RecordedSessionHostState currentState;
                    lock (scopesGate)
                    {
                        currentState = CurrentState;
                    }

                    scope.UpdateHostState(currentState);
                }

                RebuildExtensionSlotsImmediately();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await DisposeScopesCoreAsync();
                throw;
            }
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public void UpdateHostState(RecordedSessionHostState state)
    {
        ThrowIfDisposed();
        UpdateHostStateCore(state);
    }

    private void UpdateHostStateCore(RecordedSessionHostState state)
    {
        RecordedSessionExtensionScopeRegistration[] scopeSnapshot;
        lock (scopesGate)
        {
            CurrentState = state;
            scopeSnapshot = scopes.ToArray();
        }

        stateChanged.OnNext(state);

        foreach (var scope in scopeSnapshot)
        {
            scope.Scope.UpdateHostState(state);
        }
    }

    public async ValueTask DisposeScopesAsync()
    {
        if (disposed)
        {
            return;
        }

        await lifecycleGate.WaitAsync();
        try
        {
            if (disposed)
            {
                return;
            }

            await DisposeScopesCoreAsync();
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    private async ValueTask DisposeScopesCoreAsync()
    {
        operationCoordinator.CancelCurrent();
        IDisposable[] subscriptionSnapshot;
        RecordedSessionExtensionScopeRegistration[] scopeSnapshot;
        lock (scopesGate)
        {
            subscriptionSnapshot = slotSubscriptions.ToArray();
            slotSubscriptions.Clear();
            extensionSlotsRebuildQueued = false;
            scopeSnapshot = scopes.ToArray();
            scopes.Clear();
        }

        foreach (var subscription in subscriptionSnapshot)
        {
            subscription.Dispose();
        }

        ClearExtensionSlots();
        for (var i = scopeSnapshot.Length - 1; i >= 0; i--)
        {
            await scopeSnapshot[i].Scope.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        await lifecycleGate.WaitAsync();
        try
        {
            if (disposed)
            {
                return;
            }

            lock (scopesGate)
            {
                disposed = true;
            }

            await DisposeScopesCoreAsync();
            await operationCoordinator.DisposeAsync();
            stateChanged.Dispose();
        }
        finally
        {
            lifecycleGate.Release();
        }
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
            operations);
    }

    private void AttachScopeSlots(RecordedSessionExtensionSlots slots)
    {
        slotSubscriptions.Add(slots.SubscribeToChanges(QueueExtensionSlotsRebuild));
    }

    private void QueueExtensionSlotsRebuild()
    {
        lock (scopesGate)
        {
            if (disposed || extensionSlotsRebuildQueued)
            {
                return;
            }

            extensionSlotsRebuildQueued = true;
        }

        uiThreadDispatcher.Post(() =>
        {
            lock (scopesGate)
            {
                if (disposed || !extensionSlotsRebuildQueued)
                {
                    return;
                }

                extensionSlotsRebuildQueued = false;
            }

            RebuildExtensionSlots();
        });
    }

    private void RebuildExtensionSlotsImmediately()
    {
        lock (scopesGate)
        {
            extensionSlotsRebuildQueued = false;
        }

        RebuildExtensionSlots();
    }

    private void RebuildExtensionSlots()
    {
        RecordedSessionExtensionScopeRegistration[] scopeSnapshot;
        lock (scopesGate)
        {
            scopeSnapshot = scopes.ToArray();
        }

        extensionSlotPublisher.Publish(builder =>
        {
            foreach (var scope in scopeSnapshot)
            {
                ExtensionContributionValidator.ValidateRecordedSessionSlots(
                    scope.Scope.Slots,
                    scope.ExtensionId);
                builder.AddFrom(scope.Scope.Slots);
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

    private sealed record RecordedSessionExtensionScopeRegistration(
        string ExtensionId,
        IRecordedSessionExtensionScope Scope);
}

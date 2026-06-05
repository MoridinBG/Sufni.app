using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.Models;
using Sufni.App.SessionDetails;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Tests.ExtensionHost;

public class RecordedSessionExtensionManagerTests
{
    [Fact]
    public async Task InitializeAsync_CreatesInitializesAndUpdatesScopesInFactoryOrder()
    {
        var first = new TestRecordedSessionExtensionFactory("first");
        var second = new TestRecordedSessionExtensionFactory("second");
        var manager = CreateManager([first, second]);
        var state = CreateState();

        await manager.InitializeAsync(state);

        Assert.NotNull(first.Scope);
        Assert.NotNull(second.Scope);
        Assert.True(first.Scope.Initialized);
        Assert.True(second.Scope.Initialized);
        Assert.Equal([state], first.Scope.UpdatedStates);
        Assert.Equal([state], second.Scope.UpdatedStates);
        Assert.Equal(first.Context!.SessionId, second.Context!.SessionId);
    }

    [Fact]
    public async Task UpdateHostState_PushesStateToScopesAndStateChangedObservable()
    {
        var factory = new TestRecordedSessionExtensionFactory("test");
        var manager = CreateManager([factory]);
        var initial = CreateState(isLoaded: true);
        await manager.InitializeAsync(initial);
        var observed = new List<RecordedSessionHostState>();
        using var subscription = factory.Context!.StateChanged.Skip(1).Subscribe(observed.Add);
        var updated = initial with
        {
            Selection = initial.Selection with { AnalysisRange = new TelemetryTimeRange(1, 3) },
            Timeline = initial.Timeline with { TelemetryDurationSeconds = 5 },
        };

        manager.UpdateHostState(updated);

        Assert.Equal(updated, manager.CurrentState);
        Assert.Equal([initial, updated], factory.Scope!.UpdatedStates);
        Assert.Equal([updated], observed);
    }

    [Fact]
    public async Task HostContext_DelegatesControlledHostOperations()
    {
        var calls = new List<string>();
        var factory = new TestRecordedSessionExtensionFactory("test");
        var manager = CreateManager(
            [factory],
            setAnalysisRange: (start, end) => calls.Add(FormattableString.Invariant($"analysis:{start}:{end}")),
            clearAnalysisRange: () => calls.Add("clear-analysis"),
            setTimelineVisibleRange: (start, end, source) => calls.Add(FormattableString.Invariant($"timeline:{start}:{end}:{source}")),
            addError: message => calls.Add($"error:{message}"),
            addNotification: message => calls.Add($"notification:{message}"),
            requestPageSelection: id => calls.Add($"page:{id}"));
        await manager.InitializeAsync(CreateState());
        var context = factory.Context;

        context!.SetAnalysisRange(1, 2);
        context.ClearAnalysisRange();
        context.SetTimelineVisibleRange(0.2, 0.8, "source");
        context.AddError("bad");
        context.AddNotification("done");
        context.RequestPageSelection("extension.page");

        Assert.Equal(
            [
                "analysis:1:2",
                "clear-analysis",
                "timeline:0.2:0.8:source",
                "error:bad",
                "notification:done",
                "page:extension.page",
            ],
            calls);
    }

    [Fact]
    public async Task HostContext_StartOperation_UsesOperationCoordinator()
    {
        var factory = new TestRecordedSessionExtensionFactory("test");
        var reports = new List<(string Message, double Percent)>();
        var completed = 0;
        var manager = CreateManager(
            [factory],
            operationCoordinator: new RecordedSessionOperationCoordinator(
                (message, percent) => reports.Add((message, percent)),
                () => completed++));
        await manager.InitializeAsync(CreateState());

        var first = factory.Context!.StartOperation("first");
        var second = factory.Context.StartOperation("second");
        first.Report("stale", 20);
        second.Report("active", 40);
        first.Complete();
        second.Complete();

        Assert.Equal([("first", 0), ("second", 0), ("active", 40)], reports);
        Assert.True(first.CancellationToken.IsCancellationRequested);
        Assert.False(first.IsCurrent);
        Assert.False(second.IsCurrent);
        Assert.Equal(1, completed);
    }

    [Fact]
    public async Task ScopeSlotChanges_AreMirroredToHostSlotsAndClearedOnDispose()
    {
        var factory = new TestRecordedSessionExtensionFactory("test");
        var manager = CreateManager([factory]);
        await manager.InitializeAsync(CreateState(isLoaded: true));
        var contribution = new RecordedSessionToolbarContribution(
            "test",
            "toolbar",
            Order: 1,
            RecordedSessionToolbarZone.Trailing,
            new object());
        var metricContribution = new RecordedSessionStatisticsMetricContribution(
            "test",
            "metric",
            Order: 2,
            RecordedSessionStatisticsMetricTarget.FrontLscPercentage,
            "match 12.00",
            "-2.00",
            RecordedSessionMetricTone.Negative);

        factory.Scope!.Slots.GraphToolbarActions.Add(contribution);
        factory.Scope.Slots.StatisticsMetrics.Add(metricContribution);

        Assert.Equal([contribution], manager.ExtensionSlots.GraphToolbarActions);
        Assert.Equal([metricContribution], manager.ExtensionSlots.StatisticsMetrics);

        await manager.DisposeScopesAsync();

        Assert.Empty(manager.ExtensionSlots.GraphToolbarActions);
        Assert.Empty(manager.ExtensionSlots.StatisticsMetrics);
    }

    [Fact]
    public async Task ScopeSlotChanges_AreCoalescedBeforeMirroringToHostSlots()
    {
        var dispatcher = new DeferredUiThreadDispatcher();
        var factory = new TestRecordedSessionExtensionFactory("test");
        var manager = CreateManager([factory], uiThreadDispatcher: dispatcher);
        await manager.InitializeAsync(CreateState(isLoaded: true));
        var first = new RecordedSessionToolbarContribution(
            "test",
            "first",
            Order: 1,
            RecordedSessionToolbarZone.Trailing,
            new object());
        var second = first with { ContributionId = "second", Order = 2 };

        factory.Scope!.Slots.GraphToolbarActions.Add(first);
        factory.Scope.Slots.GraphToolbarActions.Add(second);

        Assert.Equal(1, dispatcher.PendingPostCount);
        Assert.Empty(manager.ExtensionSlots.GraphToolbarActions);

        dispatcher.RunPendingPosts();

        Assert.Equal([first, second], manager.ExtensionSlots.GraphToolbarActions);
    }

    [Fact]
    public async Task DisposeScopesAsync_DisposesScopesAndAllowsReinitialize()
    {
        var factory = new TestRecordedSessionExtensionFactory("test");
        var manager = CreateManager([factory]);
        await manager.InitializeAsync(CreateState(isLoaded: true));
        var firstScope = factory.Scope!;

        await manager.DisposeScopesAsync();
        await manager.InitializeAsync(CreateState(isLoaded: true, isActive: true));

        Assert.True(firstScope.Disposed);
        Assert.NotSame(firstScope, factory.Scope);
        Assert.True(factory.Scope!.Initialized);
    }

    private static RecordedSessionExtensionManager CreateManager(
        IReadOnlyList<IRecordedSessionExtensionFactory> factories,
        RecordedSessionOperationCoordinator? operationCoordinator = null,
        Action<double, double>? setAnalysisRange = null,
        Action? clearAnalysisRange = null,
        Action<double, double, object>? setTimelineVisibleRange = null,
        Action<string>? addError = null,
        Action<string>? addNotification = null,
        Action<string>? requestPageSelection = null,
        IUiThreadDispatcher? uiThreadDispatcher = null)
    {
        return new RecordedSessionExtensionManager(
            Guid.NewGuid(),
            factories,
            Substitute.For<IExtensionDatabaseConnection>(),
            Substitute.For<IRecordedSessionDataReader>(),
            new InlineBackgroundTaskRunner(),
            uiThreadDispatcher ?? new InlineUiThreadDispatcher(),
            operationCoordinator ?? new RecordedSessionOperationCoordinator((_, _) => { }, () => { }),
            setAnalysisRange ?? ((_, _) => { }),
            clearAnalysisRange ?? (() => { }),
            setTimelineVisibleRange ?? ((_, _, _) => { }),
            addError ?? (_ => { }),
            addNotification ?? (_ => { }),
            requestPageSelection ?? (_ => { }));
    }

    private static RecordedSessionHostState CreateState(
        bool isLoaded = false,
        bool isActive = false)
    {
        var snapshot = TestSnapshots.Session(hasProcessedData: true);
        return new RecordedSessionHostState(
            new RecordedSessionIdentityState(
                snapshot.Id,
                snapshot.Name,
                snapshot.Timestamp,
                snapshot.DurationSeconds,
                isLoaded,
                isActive),
            new RecordedSessionSelectionState(null),
            new RecordedSessionTimelineState(null, snapshot.DurationSeconds, null),
            new RecordedSessionStatisticsState(
                SessionDamperPercentages.Empty,
                DampingSpeedCutoffs.Default,
                VelocityAverageMode.SampleAveraged,
                TravelHistogramMode.ActiveSuspension));
    }

    private sealed class TestRecordedSessionExtensionFactory(string extensionId) : IRecordedSessionExtensionFactory
    {
        public string ExtensionId { get; } = extensionId;
        public RecordedSessionHostContext? Context { get; private set; }
        public TestRecordedSessionExtensionScope? Scope { get; private set; }

        public IRecordedSessionExtensionScope Create(RecordedSessionHostContext context)
        {
            Context = context;
            Scope = new TestRecordedSessionExtensionScope();
            return Scope;
        }
    }

    private sealed class TestRecordedSessionExtensionScope : IRecordedSessionExtensionScope
    {
        public bool Initialized { get; private set; }
        public bool Disposed { get; private set; }
        public RecordedSessionExtensionSlots Slots { get; } = new();
        public List<RecordedSessionHostState> UpdatedStates { get; } = [];

        public ValueTask InitializeAsync(CancellationToken cancellationToken)
        {
            Initialized = true;
            return ValueTask.CompletedTask;
        }

        public void UpdateHostState(RecordedSessionHostState state)
        {
            UpdatedStates.Add(state);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DeferredUiThreadDispatcher : IUiThreadDispatcher
    {
        private readonly Queue<Action> pendingPosts = new();

        public int PendingPostCount => pendingPosts.Count;

        public bool CheckAccess() => true;

        public void Post(Action action)
        {
            pendingPosts.Enqueue(action);
        }

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action) => action();

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());

        public void RunPendingPosts()
        {
            while (pendingPosts.Count > 0)
            {
                pendingPosts.Dequeue()();
            }
        }
    }
}

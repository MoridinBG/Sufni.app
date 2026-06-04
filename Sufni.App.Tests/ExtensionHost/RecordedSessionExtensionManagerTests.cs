using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.ExtensionHost.Database;
using Sufni.App.ExtensionHost.RecordedSessions;
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
            AnalysisRange = new TelemetryTimeRange(1, 3),
            TelemetryDurationSeconds = 5,
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
        Action<string>? requestPageSelection = null)
    {
        return new RecordedSessionExtensionManager(
            Guid.NewGuid(),
            factories,
            Substitute.For<IExtensionDatabaseConnection>(),
            Substitute.For<IDatabaseService>(),
            new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(),
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
            snapshot,
            Domain: null,
            AnalysisRange: null,
            TrackTimelineContext: null,
            TelemetryDurationSeconds: snapshot.DurationSeconds,
            IsLoaded: isLoaded,
            IsActive: isActive);
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
}

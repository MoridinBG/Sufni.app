using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Extensibility.RecordedSessions;

[Collection("Ui")]
public class RecordedSessionExtensionManagerTests
{
    [Fact]
    public async Task InitializeAsync_CreatesInitializesAndUpdatesScopesInFactoryOrder()
    {
        var first = new TestRecordedSessionExtensionFactory("first");
        var second = new TestRecordedSessionExtensionFactory("second");
        var manager = CreateManager([first, second]);
        var state = CreateState();

        await manager.InitializeAsync(state, cancellationToken: TestContext.Current.CancellationToken);

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
        await manager.InitializeAsync(initial, cancellationToken: TestContext.Current.CancellationToken);
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
            addNotification: message => calls.Add($"notification:{message}"));
        await manager.InitializeAsync(CreateState(), cancellationToken: TestContext.Current.CancellationToken);
        var context = factory.Context;

        context!.SetAnalysisRange(1, 2);
        context.ClearAnalysisRange();
        context.SetTimelineVisibleRange(0.2, 0.8, "source");
        context.AddError("bad");
        context.AddNotification("done");

        Assert.Equal(
            [
                "analysis:1:2",
                "clear-analysis",
                "timeline:0.2:0.8:source",
                "error:bad",
                "notification:done",
            ],
            calls);
    }

    [Fact]
    public void Constructor_RejectsDuplicateRecordedSessionFactoryExtensionIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            CreateManager(
            [
                new TestRecordedSessionExtensionFactory("duplicate"),
                new TestRecordedSessionExtensionFactory("duplicate"),
            ]));

        Assert.Contains("duplicate", exception.Message);
    }

    [Fact]
    public async Task DisposeScopesAsync_DisposesScopesAndAllowsReinitialize()
    {
        var factory = new TestRecordedSessionExtensionFactory("test");
        var manager = CreateManager([factory]);
        await manager.InitializeAsync(CreateState(isLoaded: true), cancellationToken: TestContext.Current.CancellationToken);
        var firstScope = factory.Scope!;

        await manager.DisposeScopesAsync();
        await manager.InitializeAsync(CreateState(isLoaded: true, isActive: true), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(firstScope.Disposed);
        Assert.NotSame(firstScope, factory.Scope);
        Assert.True(factory.Scope!.Initialized);
    }

    private static RecordedSessionExtensionManager CreateManager(
        IReadOnlyList<IRecordedSessionExtensionFactory> factories,
        Action<double, double>? setAnalysisRange = null,
        Action? clearAnalysisRange = null,
        Action<double, double, object>? setTimelineVisibleRange = null,
        Action<string>? addError = null,
        Action<string>? addNotification = null,
        IUiThreadDispatcher? uiThreadDispatcher = null)
    {
        return new RecordedSessionExtensionManager(
            Guid.NewGuid(),
            factories,
            Substitute.For<IExtensionDatabaseConnection>(),
            new InlineBackgroundTaskRunner(),
            uiThreadDispatcher ?? new InlineUiThreadDispatcher(),
            new DelegatingRecordedSessionHostOperations(
                setAnalysisRange,
                clearAnalysisRange,
                setTimelineVisibleRange,
                addError,
                addNotification));
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
            new RecordedSessionAnalysisState(
                SessionDampingPercentages.Empty,
                DampingSpeedCutoffs.Default,
                VelocityAverageMode.SampleAveraged,
                TravelDistributionMode.ActiveSuspension));
    }
}

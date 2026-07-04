using System.Reactive.Linq;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.Database;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Extensibility.RecordedSessions;

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
        var contribution = new RecordedSessionToolbarViewContribution(
            "test",
            "toolbar",
            Order: 1,
            RecordedSessionToolbarZone.Trailing,
            new TestContributionViewModel());
        var metricContribution = new RecordedSessionAnalysisMetricContribution(
            "test",
            "metric",
            Order: 2,
            RecordedSessionAnalysisMetricTarget.FrontLscPercentage,
            "match 12.00",
            "-2.00",
            RecordedSessionMetricTone.Negative);
        var analysisTabContribution = CreateAnalysisTabContribution("test", "tab");

        factory.Scope!.Slots.SignalToolbarViews.Add(contribution);
        factory.Scope.Slots.AnalysisMetrics.Add(metricContribution);
        factory.Scope.Slots.AnalysisTabs.Add(analysisTabContribution);

        Assert.Equal([contribution], manager.ExtensionSlots.SignalToolbarViews);
        Assert.Equal([metricContribution], manager.ExtensionSlots.AnalysisMetrics);
        Assert.Equal([analysisTabContribution], manager.ExtensionSlots.AnalysisTabs);

        await manager.DisposeScopesAsync();

        Assert.Empty(manager.ExtensionSlots.SignalToolbarViews);
        Assert.Empty(manager.ExtensionSlots.AnalysisMetrics);
        Assert.Empty(manager.ExtensionSlots.AnalysisTabs);
    }

    [Fact]
    public async Task ScopeSlotChanges_MirrorAndClearHostedSignalRowWithNeutralSeriesViewModel()
    {
        var factory = new TestRecordedSessionExtensionFactory("owner");
        var manager = CreateManager([factory]);
        await manager.InitializeAsync(CreateState(isLoaded: true));
        var contribution = new RecordedSessionHostedSignalRowContribution(
            "owner",
            "neutral-signal",
            Order: 1,
            RecordedSessionBuiltInSignalRow.Travel,
            RecordedSessionSignalRowTarget.Extension("owner", "neutral-signal"),
            "Matched travel",
            SurfacePresentationState.Ready,
            new RecordedSessionSignalPlotViewModel(
                [], invertValueAxis: true, durationSeconds: 1, emptyMessage: "none", airtimeSpans: []),
            IsInitiallyExpanded: false);

        factory.Scope!.Slots.HostedSignalRows.Add(contribution);

        Assert.Equal([contribution], manager.ExtensionSlots.HostedSignalRows);

        await manager.DisposeScopesAsync();

        Assert.Empty(manager.ExtensionSlots.HostedSignalRows);
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
    public async Task ScopeSlotChanges_AcceptsAnalysisTabContributionFromOwner()
    {
        var factory = new TestRecordedSessionExtensionFactory("owner");
        var manager = CreateManager([factory]);
        await manager.InitializeAsync(CreateState(isLoaded: true));
        var contribution = CreateAnalysisTabContribution("owner", "tab");

        factory.Scope!.Slots.AnalysisTabs.Add(contribution);

        Assert.Equal([contribution], manager.ExtensionSlots.AnalysisTabs);
    }

    [Fact]
    public async Task ScopeSlotChanges_RejectAnalysisTabContributionFromDifferentOwner()
    {
        var factory = new TestRecordedSessionExtensionFactory("owner");
        var manager = CreateManager([factory]);
        await manager.InitializeAsync(CreateState(isLoaded: true));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Scope!.Slots.AnalysisTabs.Add(CreateAnalysisTabContribution("other", "tab")));

        Assert.Contains("other:tab", exception.Message);
        Assert.Contains("owner", exception.Message);
        Assert.Empty(manager.ExtensionSlots.AnalysisTabs);
    }

    [Fact]
    public async Task ScopeSlotChanges_RejectAnalysisTabContributionWithBlankContributionId()
    {
        var factory = new TestRecordedSessionExtensionFactory("owner");
        var manager = CreateManager([factory]);
        await manager.InitializeAsync(CreateState(isLoaded: true));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Scope!.Slots.AnalysisTabs.Add(CreateAnalysisTabContribution("owner", " ")));

        Assert.Contains("recorded-session analysis tabs contribution id is required", exception.Message);
        Assert.Empty(manager.ExtensionSlots.AnalysisTabs);
    }

    [Fact]
    public async Task ScopeSlotChanges_RejectDuplicateContributionIdsAcrossAnalysisTabsAndOtherSlotFamilies()
    {
        var factory = new TestRecordedSessionExtensionFactory("owner");
        var manager = CreateManager([factory]);
        await manager.InitializeAsync(CreateState(isLoaded: true));
        factory.Scope!.Slots.AnalysisTabs.Add(CreateAnalysisTabContribution("owner", "duplicate"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Scope.Slots.Pages.Add(new RecordedSessionPageContribution(
                "owner",
                "duplicate",
                Order: 2,
                "Page",
                new TestContributionViewModel(),
                RequestedIndex: 0)));

        Assert.Contains("duplicate", exception.Message);
        Assert.Contains("owner", exception.Message);
        Assert.Empty(manager.ExtensionSlots.Pages);
    }

    [Fact]
    public async Task ScopeSlotChanges_RejectContributionFromDifferentOwner()
    {
        var factory = new TestRecordedSessionExtensionFactory("owner");
        var manager = CreateManager([factory]);
        await manager.InitializeAsync(CreateState(isLoaded: true));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Scope!.Slots.SignalToolbarViews.Add(new RecordedSessionToolbarViewContribution(
                "other",
                "toolbar",
                Order: 1,
                RecordedSessionToolbarZone.Trailing,
                new TestContributionViewModel())));

        Assert.Contains("other:toolbar", exception.Message);
        Assert.Contains("owner", exception.Message);
        Assert.Empty(manager.ExtensionSlots.SignalToolbarViews);
    }

    [Fact]
    public async Task ScopeSlotChanges_RejectDuplicateContributionIdsAcrossSlotFamilies()
    {
        var factory = new TestRecordedSessionExtensionFactory("owner");
        var manager = CreateManager([factory]);
        await manager.InitializeAsync(CreateState(isLoaded: true));
        factory.Scope!.Slots.SignalToolbarViews.Add(new RecordedSessionToolbarViewContribution(
            "owner",
            "duplicate",
            Order: 1,
            RecordedSessionToolbarZone.Trailing,
            new TestContributionViewModel()));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Scope.Slots.MediaPanes.Add(new RecordedSessionMediaPaneContribution(
                "owner",
                "duplicate",
                Order: 2,
                new TestContributionViewModel())));

        Assert.Contains("duplicate", exception.Message);
        Assert.Contains("owner", exception.Message);
        Assert.Empty(manager.ExtensionSlots.MediaPanes);
    }

    [Fact]
    public async Task ScopeSlotChanges_RejectHostedSignalRowWithInvalidRowTarget()
    {
        var factory = new TestRecordedSessionExtensionFactory("owner");
        var manager = CreateManager([factory]);
        await manager.InitializeAsync(CreateState(isLoaded: true));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            factory.Scope!.Slots.HostedSignalRows.Add(new RecordedSessionHostedSignalRowContribution(
                "owner",
                "hosted-row",
                Order: 1,
                RecordedSessionBuiltInSignalRow.Travel,
                RecordedSessionSignalRowTarget.BuiltIn(RecordedSessionBuiltInSignalRow.Velocity),
                "Hosted",
                SurfacePresentationState.Ready,
                new TestContributionViewModel(),
                IsInitiallyExpanded: true)));

        Assert.Contains("hosted-row", exception.Message);
        Assert.Empty(manager.ExtensionSlots.HostedSignalRows);
    }

    [Fact]
    public async Task ScopeSlotChanges_AreCoalescedBeforeMirroringToHostSlots()
    {
        var dispatcher = new DeferredUiThreadDispatcher();
        var factory = new TestRecordedSessionExtensionFactory("test");
        var manager = CreateManager([factory], uiThreadDispatcher: dispatcher);
        await manager.InitializeAsync(CreateState(isLoaded: true));
        var first = new RecordedSessionToolbarViewContribution(
            "test",
            "first",
            Order: 1,
            RecordedSessionToolbarZone.Trailing,
            new TestContributionViewModel());
        var second = first with { ContributionId = "second", Order = 2 };

        factory.Scope!.Slots.SignalToolbarViews.Add(first);
        factory.Scope.Slots.SignalToolbarViews.Add(second);

        Assert.Equal(1, dispatcher.PendingPostCount);
        Assert.Empty(manager.ExtensionSlots.SignalToolbarViews);

        dispatcher.RunPendingPosts();

        Assert.Equal([first, second], manager.ExtensionSlots.SignalToolbarViews);
    }

    [Fact]
    public async Task ExtensionSlots_RemainsStableAcrossStateUpdatesSlotMirrorsAndReinitialize()
    {
        var factory = new TestRecordedSessionExtensionFactory("test");
        var manager = CreateManager([factory]);
        var hostSlots = manager.ExtensionSlots;
        var contribution = new RecordedSessionToolbarViewContribution(
            "test",
            "toolbar",
            Order: 1,
            RecordedSessionToolbarZone.Trailing,
            new TestContributionViewModel());

        await manager.InitializeAsync(CreateState(isLoaded: true));
        manager.UpdateHostState(CreateState(isLoaded: true, isActive: true));
        factory.Scope!.Slots.SignalToolbarViews.Add(contribution);

        Assert.Same(hostSlots, manager.ExtensionSlots);
        Assert.Equal([contribution], hostSlots.SignalToolbarViews);

        await manager.DisposeScopesAsync();

        Assert.Same(hostSlots, manager.ExtensionSlots);
        Assert.Empty(hostSlots.SignalToolbarViews);

        await manager.InitializeAsync(CreateState(isLoaded: true));

        Assert.Same(hostSlots, manager.ExtensionSlots);
        Assert.Empty(hostSlots.SignalToolbarViews);
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
        var coordinator = operationCoordinator ?? new RecordedSessionOperationCoordinator((_, _) => { }, () => { });
        return new RecordedSessionExtensionManager(
            Guid.NewGuid(),
            factories,
            Substitute.For<IExtensionDatabaseConnection>(),
            Substitute.For<IRecordedSessionDataReader>(),
            new InlineBackgroundTaskRunner(),
            uiThreadDispatcher ?? new InlineUiThreadDispatcher(),
            coordinator,
            new DelegatingRecordedSessionHostOperations(
                setAnalysisRange,
                clearAnalysisRange,
                setTimelineVisibleRange,
                addError,
                addNotification,
                coordinator.StartOperation,
                requestPageSelection));
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

    private static RecordedSessionAnalysisTabContribution CreateAnalysisTabContribution(
        string extensionId,
        string contributionId)
    {
        return new RecordedSessionAnalysisTabContribution(
            extensionId,
            contributionId,
            Order: 1,
            "Extension tab",
            RequestedIndex: 3,
            new TestContributionViewModel());
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

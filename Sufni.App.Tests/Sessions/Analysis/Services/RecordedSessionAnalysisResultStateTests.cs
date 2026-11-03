using Sufni.App.Sessions.Plots;
using Sufni.App.Bikes.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;
using Sufni.App.Shared.Models;

namespace Sufni.App.Tests.Sessions.Analysis.Services;

public class RecordedSessionAnalysisResultStateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerCompletion_CommitsCacheAndNotificationTogetherOnTheDispatcher(bool superseded)
    {
        var dispatcher = new ManualUiThreadDispatcher();
        var runner = new Sufni.App.Tests.TestSupport.Async.DeferredBackgroundTaskRunner();
        using var state = new RecordedSessionAnalysisResultState(new TestAnalysisComputer(), runner, dispatcher, () => new TelemetryData());
        var inputs = CreateInputs(null);
        state.Invalidate(inputs);
        var changes = new List<RecordedSessionAnalysisResultChanged>();
        using var subscription = state.Connect().Subscribe(change =>
        {
            Assert.True(dispatcher.CheckAccess());
            Assert.Equal(11, Assert.IsType<DampingPercentagesAnalysisResult>(change.Result).Percentages.FrontHscPercentage);
            Assert.Equal(11, Assert.IsType<DampingPercentagesAnalysisResult>(state.Get(change.Key)).Percentages.FrontHscPercentage);
            changes.Add(change);
        });
        var request = state.RequestAsync(inputs.DampingPercentagesKey, TestContext.Current.CancellationToken);

        await runner.RunNextAsync();
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        Assert.False(request.IsCompleted);
        Assert.Null(state.Get(inputs.DampingPercentagesKey));
        if (superseded) state.Invalidate(inputs with { TelemetryGeneration = inputs.TelemetryGeneration + 1 });
        await dispatcher.DrainAsync();
        await request.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        if (superseded)
        {
            Assert.Empty(changes);
            Assert.Null(state.Get(inputs.DampingPercentagesKey));
        }
        else Assert.Single(changes);
    }

    [Fact]
    public async Task DisposeAsync_WaitsForOwnedWorkAndSuppressesItsLateResult()
    {
        var computer = new BlockingAnalysisComputer();
        var state = new RecordedSessionAnalysisResultState(computer, new NonCancellingBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(), () => new TelemetryData());
        var inputs = CreateInputs(null);
        state.Invalidate(inputs);
        var changes = new List<RecordedSessionAnalysisResultChanged>();
        using var subscription = state.Connect().Subscribe(changes.Add);
        var request = state.RequestAsync(inputs.DampingPercentagesKey, TestContext.Current.CancellationToken);
        await computer.Started.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var disposal = state.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        computer.Complete();
        await disposal.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await request.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(changes);
        Assert.Null(state.CurrentInputs);
        Assert.Null(state.Get(inputs.DampingPercentagesKey));
    }

    [Fact]
    public async Task CalculationFailure_PublishesTypedDiagnosticsWithoutCachingAResult()
    {
        var computer = new FailingAnalysisComputer();
        using var state = new RecordedSessionAnalysisResultState(computer, new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(), () => TestTelemetryData.CreateMinimal());
        var inputs = CreateInputs(null);
        state.Invalidate(inputs);
        var changes = new List<RecordedSessionAnalysisResultChanged>();
        using var subscription = state.Connect().Subscribe(changes.Add);

        await state.RequestAsync(inputs.DampingPercentagesKey, TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        var failure = Assert.Single(changes).Failure;
        Assert.NotNull(failure);
        Assert.Equal(SessionEditorFailureReason.Analysis, failure.Reason);
        Assert.Equal("calculation diagnostic", failure.Details);
        Assert.Null(state.Get(inputs.DampingPercentagesKey));
    }

    [Fact]
    public async Task RequestAsync_PublishesAgain_WhenSynchronousRequestReusesPreviousKey()
    {
        var telemetry = new TelemetryData();
        using var state = new RecordedSessionAnalysisResultState(
            new TestAnalysisComputer(),
            new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var changes = new List<RecordedSessionAnalysisResultChanged>();
        using var subscription = state.Connect().Subscribe(changes.Add);

        var fullInputs = CreateInputs(range: null);
        var rangedInputs = CreateInputs(new TelemetryTimeRange(0, 1));

        state.Invalidate(fullInputs);
        await state.RequestAsync(fullInputs.DampingPercentagesKey, cancellationToken: TestContext.Current.CancellationToken);
        state.Invalidate(rangedInputs);
        await state.RequestAsync(rangedInputs.DampingPercentagesKey, cancellationToken: TestContext.Current.CancellationToken);
        state.Invalidate(fullInputs);
        await state.RequestAsync(fullInputs.DampingPercentagesKey, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(3, changes.Count);
        var result = Assert.IsType<DampingPercentagesAnalysisResult>(changes[^1].Result);
        Assert.Equal(11, result.Percentages.FrontHscPercentage);
    }

    [Fact]
    public async Task Invalidate_RetainsCachedResult_WhenNewInputsStillMatchKey()
    {
        var telemetry = new TelemetryData();
        var computer = new TestAnalysisComputer();
        using var state = new RecordedSessionAnalysisResultState(
            computer,
            new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var changes = new List<RecordedSessionAnalysisResultChanged>();
        using var subscription = state.Connect().Subscribe(changes.Add);
        var inputs = CreateInputs(range: null);
        var key = inputs.DampingPercentagesKey;
        state.Invalidate(inputs);
        await state.RequestAsync(key, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(11, Assert.IsType<DampingPercentagesAnalysisResult>(state.Get(key)).Percentages.FrontHscPercentage);
        changes.Clear();

        var updatedInputs = inputs with { TravelDistributionMode = TravelDistributionMode.DynamicSag };
        state.Invalidate(updatedInputs);
        await state.RequestAsync(updatedInputs.DampingPercentagesKey, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, computer.ComputeCount);
        Assert.Equal(11, Assert.IsType<DampingPercentagesAnalysisResult>(state.Get(updatedInputs.DampingPercentagesKey)).Percentages.FrontHscPercentage);
        var change = Assert.Single(changes);
        Assert.Equal(11, Assert.IsType<DampingPercentagesAnalysisResult>(change.Result).Percentages.FrontHscPercentage);
    }

    [Fact]
    public async Task RequestAsync_CallerCancellationDoesNotCancelSharedInFlightComputation()
    {
        var telemetry = new TelemetryData();
        var computer = new TestAnalysisComputer();
        var backgroundTaskRunner = new Sufni.App.Tests.TestSupport.Async.DeferredBackgroundTaskRunner();
        using var state = new RecordedSessionAnalysisResultState(
            computer,
            backgroundTaskRunner,
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var changes = new List<RecordedSessionAnalysisResultChanged>();
        using var subscription = state.Connect().Subscribe(changes.Add);
        var inputs = CreateInputs(range: null);
        var key = inputs.DampingPercentagesKey;
        using var callerCancellation = new CancellationTokenSource();
        state.Invalidate(inputs);

        var firstRequest = state.RequestAsync(key, callerCancellation.Token);
        var secondRequest = state.RequestAsync(key, cancellationToken: TestContext.Current.CancellationToken);
        await callerCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRequest);
        await backgroundTaskRunner.RunNextAsync();
        await secondRequest.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, computer.ComputeCount);
        var change = Assert.Single(changes);
        Assert.Equal(key, change.Key);
        Assert.Equal(11, Assert.IsType<DampingPercentagesAnalysisResult>(change.Result).Percentages.FrontHscPercentage);
    }

    [Fact]
    public async Task Invalidate_SuppressesCompletionFromStaleInputs_WhenWorkIgnoresCancellation()
    {
        var telemetry = new TelemetryData();
        var computer = new BlockingAnalysisComputer();
        using var state = new RecordedSessionAnalysisResultState(
            computer,
            new NonCancellingBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var changes = new List<RecordedSessionAnalysisResultChanged>();
        using var subscription = state.Connect().Subscribe(changes.Add);
        var fullInputs = CreateInputs(range: null);
        var rangedInputs = CreateInputs(new TelemetryTimeRange(0, 1));
        var staleKey = fullInputs.DampingPercentagesKey;

        state.Invalidate(fullInputs);
        var staleRequest = state.RequestAsync(staleKey, cancellationToken: TestContext.Current.CancellationToken);
        await computer.Started.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        state.Invalidate(rangedInputs);
        computer.Complete();
        await staleRequest.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(changes);
        Assert.Null(state.Get(staleKey));
    }

    [Fact]
    public void Invalidate_PublishesInputChanges()
    {
        var telemetry = new TelemetryData();
        using var state = new RecordedSessionAnalysisResultState(
            new TestAnalysisComputer(),
            new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var changes = new List<RecordedSessionAnalysisInputs>();
        using var subscription = state.ConnectInputs().Subscribe(changes.Add);
        var fullInputs = CreateInputs(range: null);
        var rangedInputs = CreateInputs(new TelemetryTimeRange(0, 1));

        state.Invalidate(fullInputs);
        state.Invalidate(fullInputs);
        state.Invalidate(rangedInputs);

        Assert.Equal(new[] { fullInputs, rangedInputs }, changes);
    }

    [Fact]
    public async Task RequestAsync_AfterDispose_IsNoOp()
    {
        var computer = new TestAnalysisComputer();
        var state = new RecordedSessionAnalysisResultState(computer, new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(), () => new TelemetryData());
        var inputs = CreateInputs(null);
        state.Invalidate(inputs);
        state.Dispose();
        var changes = new List<RecordedSessionAnalysisResultChanged>();
        var inputChanges = new List<RecordedSessionAnalysisInputs>();
        using var subscription = state.Connect().Subscribe(changes.Add);
        using var inputSubscription = state.ConnectInputs().Subscribe(inputChanges.Add);

        state.Invalidate(inputs);
        await state.RequestAsync(inputs.DampingPercentagesKey, TestContext.Current.CancellationToken);

        Assert.Equal(0, computer.ComputeCount);
        Assert.Empty(changes);
        Assert.Empty(inputChanges);
        Assert.Null(state.CurrentInputs);
        Assert.Null(state.Get(inputs.DampingPercentagesKey));
    }

    private static RecordedSessionAnalysisInputs CreateInputs(TelemetryTimeRange? range) =>
        new(
            TelemetryGeneration: 1,
            AnalysisRange: range,
            TravelDistributionMode: TravelDistributionMode.ActiveSuspension,
            VelocityAverageMode: VelocityAverageMode.SampleAveraged,
            BalanceDisplacementMode: BalanceDisplacementMode.Zenith,
            BalanceSpeedMode: BalanceSpeedMode.Both,
            DampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            PlotDampingSpeedCutoffs: DampingSpeedCutoffs.Default,
            SessionInsightsTargetProfile: SessionInsightsTargetProfile.Trail);

    private sealed class FailingAnalysisComputer : IRecordedSessionAnalysisComputer
    {
        public RecordedSessionAnalysisResult Compute(RecordedSessionAnalysisKey key, TelemetryData telemetry) =>
            throw new InvalidOperationException("calculation diagnostic");
    }

    private sealed class TestAnalysisComputer : IRecordedSessionAnalysisComputer
    {
        public int ComputeCount { get; private set; }

        public RecordedSessionAnalysisResult Compute(RecordedSessionAnalysisKey key, TelemetryData telemetry)
        {
            ComputeCount++;
            return new DampingPercentagesAnalysisResult(new SessionDampingPercentages(
                key.AnalysisRange.HasValue ? 10 : 11,
                null,
                null,
                null,
                null,
                null,
                null,
                null));
        }
    }

    private sealed class BlockingAnalysisComputer : IRecordedSessionAnalysisComputer
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => started.Task;

        public RecordedSessionAnalysisResult Compute(RecordedSessionAnalysisKey key, TelemetryData telemetry)
        {
            started.TrySetResult();
            completion.Task.GetAwaiter().GetResult();
            return new DampingPercentagesAnalysisResult(SessionDampingPercentages.Empty);
        }

        public void Complete() => completion.TrySetResult();
    }

    private sealed class NonCancellingBackgroundTaskRunner : IBackgroundTaskRunner
    {
        public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
            Task.Run(work);

        public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default) =>
            Task.Run(work);

        public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default) =>
            Task.Run(work);
    }

}

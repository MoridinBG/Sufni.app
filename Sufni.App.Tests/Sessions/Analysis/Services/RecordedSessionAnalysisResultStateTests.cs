using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.TestSupport.Async;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Analysis.Services;

public class RecordedSessionAnalysisResultStateTests
{
    [Fact]
    public async Task RequestAsync_PublishesAgain_WhenSynchronousRequestReusesPreviousKey()
    {
        var telemetry = new TelemetryData();
        var state = new RecordedSessionAnalysisResultState(
            new TestAnalysisComputer(),
            new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var changes = new List<RecordedSessionAnalysisResultChanged>();
        using var subscription = state.Connect().Subscribe(changes.Add);

        var fullInputs = CreateInputs(range: null);
        var rangedInputs = CreateInputs(new TelemetryTimeRange(0, 1));

        state.Invalidate(fullInputs);
        await state.RequestAsync(fullInputs.DampingPercentagesKey);
        state.Invalidate(rangedInputs);
        await state.RequestAsync(rangedInputs.DampingPercentagesKey);
        state.Invalidate(fullInputs);
        await state.RequestAsync(fullInputs.DampingPercentagesKey);

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
        await state.RequestAsync(key);
        var cached = state.Get(key);
        Assert.NotNull(cached);
        changes.Clear();

        var updatedInputs = inputs with { TravelDistributionMode = TravelDistributionMode.DynamicSag };
        state.Invalidate(updatedInputs);
        await state.RequestAsync(updatedInputs.DampingPercentagesKey);

        Assert.Equal(1, computer.ComputeCount);
        Assert.Same(cached, state.Get(updatedInputs.DampingPercentagesKey));
        var change = Assert.Single(changes);
        Assert.Same(cached, change.Result);
    }

    [Fact]
    public void RequestAsync_StartsFreshWork_WhenSameKeyIsRequestedAfterInvalidation()
    {
        var telemetry = new TelemetryData();
        var backgroundTaskRunner = new DeferredBackgroundTaskRunner();
        using var state = new RecordedSessionAnalysisResultState(
            new TestAnalysisComputer(),
            backgroundTaskRunner,
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var fullInputs = CreateInputs(range: null);
        var rangedInputs = CreateInputs(new TelemetryTimeRange(0, 1));

        state.Invalidate(fullInputs);
        _ = state.RequestAsync(fullInputs.DampingPercentagesKey);
        Assert.Equal(1, backgroundTaskRunner.RunCount);

        state.Invalidate(rangedInputs);
        state.Invalidate(fullInputs);
        _ = state.RequestAsync(fullInputs.DampingPercentagesKey);

        Assert.Equal(2, backgroundTaskRunner.RunCount);
    }

    [Fact]
    public void Invalidate_ReusesMatchingInFlightWork_WhenOnlyUnrelatedInputsChange()
    {
        var telemetry = new TelemetryData();
        var backgroundTaskRunner = new DeferredBackgroundTaskRunner();
        using var state = new RecordedSessionAnalysisResultState(
            new TestAnalysisComputer(),
            backgroundTaskRunner,
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var inputs = CreateInputs(range: null);
        state.Invalidate(inputs);
        var firstRequest = state.RequestAsync(inputs.DampingPercentagesKey);

        var updatedInputs = inputs with { TravelDistributionMode = TravelDistributionMode.DynamicSag };
        state.Invalidate(updatedInputs);
        var secondRequest = state.RequestAsync(updatedInputs.DampingPercentagesKey);

        Assert.Same(firstRequest, secondRequest);
        Assert.Equal(1, backgroundTaskRunner.RunCount);
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
        var telemetry = new TelemetryData();
        var state = new RecordedSessionAnalysisResultState(
            new TestAnalysisComputer(),
            new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var inputs = CreateInputs(range: null);
        var key = inputs.DampingPercentagesKey;
        state.Invalidate(inputs);

        state.Dispose();

        Assert.Null(state.CurrentInputs);
        Assert.Null(state.Get(key));
        state.Invalidate(inputs);
        await state.RequestAsync(key);
        using var subscription = state.Connect().Subscribe(_ => Assert.Fail("Disposed state should not publish."));
        using var inputSubscription = state.ConnectInputs().Subscribe(_ => Assert.Fail("Disposed state should not publish input changes."));
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
            DampingPercentages: SessionDampingPercentages.Empty,
            SessionInsightsTargetProfile: SessionInsightsTargetProfile.Trail);

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

    private sealed class DeferredBackgroundTaskRunner : IBackgroundTaskRunner
    {
        public int RunCount { get; private set; }

        public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default)
        {
            RunCount++;
            return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
        {
            RunCount++;
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
        {
            RunCount++;
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }
}

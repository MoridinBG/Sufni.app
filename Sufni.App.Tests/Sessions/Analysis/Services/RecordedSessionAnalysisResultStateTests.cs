using System.Security.Cryptography;
using System.Text.Json;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.TestSupport.Async;
using Sufni.App.LiveDaq.Services.Imu;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.App.Sessions.Insights.Services.SessionInsights;
using Sufni.App.Sessions.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Analysis.Services;

public class RecordedSessionAnalysisResultStateTests
{
    [Fact]
    public async Task RequestAsync_ReusesOneTypedVelocityResult_ForIdenticalSemanticKey()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        var computer = new CountingAnalysisComputer(
            new RecordedSessionAnalysisComputer(new SessionInsightsService()));
        var backgroundTaskRunner = new ControllableBackgroundTaskRunner();
        using var state = new RecordedSessionAnalysisResultState(
            computer,
            backgroundTaskRunner,
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var changes = new List<RecordedSessionAnalysisResultChanged>();
        using var subscription = state.Connect().Subscribe(changes.Add);
        var inputs = CreateInputs(range: null);
        var key = inputs.CreateKey(
            RecordedSessionAnalysisFamily.VelocityDistribution,
            SuspensionType.Front);
        state.Invalidate(inputs);

        var firstRequest = state.RequestAsync(key);
        var secondRequest = state.RequestAsync(key);
        Assert.Same(firstRequest, secondRequest);
        Assert.Equal(1, backgroundTaskRunner.RunCount);

        await backgroundTaskRunner.CompleteNextAsync();
        await Task.WhenAll(firstRequest, secondRequest);

        Assert.Equal(1, computer.ComputeCount);
        var cached = Assert.IsType<VelocityDistributionAnalysisResult>(state.Get(key));
        var initialChange = Assert.Single(changes);
        Assert.Same(cached, initialChange.Result);

        await state.RequestAsync(key);

        Assert.Equal(1, computer.ComputeCount);
        Assert.Equal(2, changes.Count);
        Assert.Same(cached, changes[^1].Result);
        var direct = RecordedSessionAnalysisComputer.CalculateVelocityDistribution(key, telemetry);
        Assert.Equal(Fingerprint(direct), Fingerprint(cached));
    }

    [Fact]
    public async Task RequestAsync_ReusesOneImuDisplayProjection_AcrossUnrelatedInputChanges()
    {
        var telemetry = TestTelemetryData.CreateWithImu();
        var computer = new CountingAnalysisComputer(
            new RecordedSessionAnalysisComputer(new SessionInsightsService()));
        var backgroundTaskRunner = new ControllableBackgroundTaskRunner();
        using var state = new RecordedSessionAnalysisResultState(
            computer,
            backgroundTaskRunner,
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var inputs = CreateInputs(range: null);
        var key = inputs.ImuDisplayProjectionKey;
        state.Invalidate(inputs);

        var firstRequest = state.RequestAsync(key);
        var secondRequest = state.RequestAsync(key);

        Assert.Same(firstRequest, secondRequest);
        Assert.Equal(1, backgroundTaskRunner.RunCount);

        await backgroundTaskRunner.CompleteNextAsync();
        await Task.WhenAll(firstRequest, secondRequest);

        var cached = Assert.IsType<ImuDisplayProjectionAnalysisResult>(state.Get(key));
        Assert.Equal(1, computer.ComputeCount);
        Assert.Equal(
            Fingerprint(ImuDisplaySignalProcessor.ProcessRecorded(telemetry)),
            Fingerprint(cached.Projection));

        var updatedInputs = inputs with
        {
            AnalysisRange = new TelemetryTimeRange(0, 1),
            TravelDistributionMode = TravelDistributionMode.DynamicSag,
            VelocityAverageMode = VelocityAverageMode.StrokePeakAveraged,
        };
        state.Invalidate(updatedInputs);
        await state.RequestAsync(updatedInputs.ImuDisplayProjectionKey);

        Assert.Equal(key, updatedInputs.ImuDisplayProjectionKey);
        Assert.Equal(1, computer.ComputeCount);
        Assert.Same(cached, state.Get(updatedInputs.ImuDisplayProjectionKey));
    }

    [Fact]
    public async Task Invalidate_ReplacesImuDisplayProjection_WhenTelemetryGenerationChanges()
    {
        var telemetry = TestTelemetryData.CreateWithImu();
        var computer = new CountingAnalysisComputer(
            new RecordedSessionAnalysisComputer(new SessionInsightsService()));
        using var state = new RecordedSessionAnalysisResultState(
            computer,
            new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var firstInputs = CreateInputs(range: null);
        var firstKey = firstInputs.ImuDisplayProjectionKey;
        state.Invalidate(firstInputs);
        await state.RequestAsync(firstKey);
        var firstResult = Assert.IsType<ImuDisplayProjectionAnalysisResult>(state.Get(firstKey));

        var nextInputs = firstInputs with { TelemetryGeneration = firstInputs.TelemetryGeneration + 1 };
        var nextKey = nextInputs.ImuDisplayProjectionKey;
        state.Invalidate(nextInputs);

        Assert.Null(state.Get(firstKey));

        await state.RequestAsync(nextKey);

        Assert.Equal(2, computer.ComputeCount);
        Assert.NotEqual(firstKey, nextKey);
        Assert.NotSame(firstResult, state.Get(nextKey));
    }

    [Fact]
    public async Task RequestAsync_KeepsVelocitySideKeysDistinct_AndDisposeReleasesOwner()
    {
        var telemetry = TestTelemetryData.CreateProcessed();
        var computer = new CountingAnalysisComputer(
            new RecordedSessionAnalysisComputer(new SessionInsightsService()));
        var state = new RecordedSessionAnalysisResultState(
            computer,
            new InlineBackgroundTaskRunner(),
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var inputs = CreateInputs(range: null);
        var frontKey = inputs.CreateKey(
            RecordedSessionAnalysisFamily.VelocityDistribution,
            SuspensionType.Front);
        var rearKey = inputs.CreateKey(
            RecordedSessionAnalysisFamily.VelocityDistribution,
            SuspensionType.Rear);
        state.Invalidate(inputs);

        await state.RequestAsync(frontKey);
        await state.RequestAsync(rearKey);

        Assert.Equal(2, computer.ComputeCount);
        Assert.IsType<VelocityDistributionAnalysisResult>(state.Get(frontKey));
        Assert.IsType<VelocityDistributionAnalysisResult>(state.Get(rearKey));
        Assert.NotSame(state.Get(frontKey), state.Get(rearKey));

        state.Dispose();
        await state.RequestAsync(frontKey);

        Assert.Null(state.CurrentInputs);
        Assert.Null(state.Get(frontKey));
        Assert.Null(state.Get(rearKey));
        Assert.Equal(2, computer.ComputeCount);
    }

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
    public void Invalidate_CancelsOnlyMismatchingInFlightWork()
    {
        var telemetry = new TelemetryData();
        var backgroundTaskRunner = new DeferredBackgroundTaskRunner();
        using var state = new RecordedSessionAnalysisResultState(
            new TestAnalysisComputer(),
            backgroundTaskRunner,
            new InlineUiThreadDispatcher(),
            () => telemetry);
        var inputs = CreateInputs(range: null);
        var travelKey = inputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        var velocityKey = inputs.CreateKey(RecordedSessionAnalysisFamily.VelocityDistribution, SuspensionType.Front);
        state.Invalidate(inputs);
        var travelRequest = state.RequestAsync(travelKey);
        _ = state.RequestAsync(velocityKey);

        var updatedInputs = inputs with { VelocityAverageMode = VelocityAverageMode.StrokePeakAveraged };
        var updatedTravelKey = updatedInputs.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
        var updatedVelocityKey = updatedInputs.CreateKey(RecordedSessionAnalysisFamily.VelocityDistribution, SuspensionType.Front);
        state.Invalidate(updatedInputs);
        var reusedTravelRequest = state.RequestAsync(updatedTravelKey);
        _ = state.RequestAsync(updatedVelocityKey);

        Assert.Same(travelRequest, reusedTravelRequest);
        Assert.Equal(3, backgroundTaskRunner.RunCount);
    }

    [Fact]
    public async Task RequestAsync_CallerCancellationDoesNotCancelSharedInFlightComputation()
    {
        var telemetry = new TelemetryData();
        var computer = new TestAnalysisComputer();
        var backgroundTaskRunner = new ControllableBackgroundTaskRunner();
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
        var secondRequest = state.RequestAsync(key);
        await callerCancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstRequest);
        await backgroundTaskRunner.CompleteNextAsync();
        await secondRequest;

        Assert.Equal(1, computer.ComputeCount);
        var change = Assert.Single(changes);
        Assert.Same(key, change.Key);
        Assert.IsType<DampingPercentagesAnalysisResult>(change.Result);
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
        var staleRequest = state.RequestAsync(staleKey);
        await computer.Started;
        state.Invalidate(rangedInputs);
        computer.Complete();
        await staleRequest;

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

    private static string Fingerprint<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private sealed class CountingAnalysisComputer(IRecordedSessionAnalysisComputer inner)
        : IRecordedSessionAnalysisComputer
    {
        public int ComputeCount { get; private set; }

        public RecordedSessionAnalysisResult Compute(RecordedSessionAnalysisKey key, TelemetryData telemetry)
        {
            ComputeCount++;
            return inner.Compute(key, telemetry);
        }
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

    private sealed class ControllableBackgroundTaskRunner : IBackgroundTaskRunner
    {
        private readonly Queue<Func<Task>> completions = new();

        public int RunCount { get; private set; }

        public Task CompleteNextAsync()
        {
            var completion = completions.Dequeue();
            return completion();
        }

        public Task RunAsync(Func<Task> work, CancellationToken cancellationToken = default)
        {
            RunCount++;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            completions.Enqueue(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await work();
                    completion.TrySetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    registration.Dispose();
                }
            });
            return completion.Task;
        }

        public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
        {
            RunCount++;
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            completions.Enqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(work());
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    registration.Dispose();
                }

                return Task.CompletedTask;
            });
            return completion.Task;
        }

        public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
        {
            RunCount++;
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            completions.Enqueue(async () =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(await work());
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    completion.TrySetCanceled(cancellationToken);
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    registration.Dispose();
                }
            });
            return completion.Task;
        }
    }
}

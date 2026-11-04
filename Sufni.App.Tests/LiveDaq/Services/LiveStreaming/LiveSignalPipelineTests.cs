using System.Collections.Concurrent;
using System.Reactive.Linq;
using Serilog.Core;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveSignalPipelineTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(33);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task BeginEpoch_DiscardsOldSamplesAndEmitsResetBeforeNewSamples()
    {
        var clock = new SignalingFakeTimeProvider();
        await using var pipeline = new LiveSignalPipeline(Interval, Logger.None, clock);
        var received = new ConcurrentQueue<LiveSignalBatch>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = pipeline.SignalBatches.Subscribe(batch =>
        {
            received.Enqueue(batch);
            if (batch.Kind == LiveSignalBatchKind.Signals) ready.TrySetResult();
        });
        pipeline.Start();
        await clock.WaitForTimerAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(Timeout);
        pipeline.AppendTravelSamples(0, [0], [10], [11]);
        pipeline.BeginEpoch(1);
        pipeline.AppendTravelSamples(0, [1], [99], [99]);
        pipeline.AppendTravelSamples(1, [0], [20], [21]);

        clock.Advance(Interval);
        await ready.Task.AwaitBoundedAsync(Timeout);

        var batches = received.ToArray();
        Assert.Equal(new[] { LiveSignalBatchKind.Reset, LiveSignalBatchKind.Signals }, batches.Select(batch => batch.Kind));
        Assert.Equal(new double[] { 20 }, batches[1].FrontTravel);
        Assert.True(batches[0].Revision < batches[1].Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BeginEpoch_DuringDelivery_KeepsEachResetAheadOfItsSamples(bool holdReset)
    {
        var clock = new SignalingFakeTimeProvider();
        await using var pipeline = new LiveSignalPipeline(Interval, Logger.None, clock);
        var received = new ConcurrentQueue<LiveSignalBatch>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = pipeline.SignalBatches.Subscribe(batch =>
        {
            received.Enqueue(batch);
            if (entered.TrySetResult()) release.Task.AwaitBoundedAsync(Timeout).GetAwaiter().GetResult();
            if (batch.FrontTravel.Contains(20)) ready.TrySetResult();
        });
        try
        {
            pipeline.Start();
            await clock.WaitForTimerAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(Timeout);
            if (holdReset)
            {
                pipeline.BeginEpoch(1);
                pipeline.AppendTravelSamples(1, [0], [10], [11]);
            }
            else pipeline.AppendTravelSamples(0, [0], [10], [11]);
            var flush = Task.Run(() => clock.Advance(Interval), TestContext.Current.CancellationToken);
            await entered.Task.AwaitBoundedAsync(Timeout);

            pipeline.BeginEpoch(2);
            pipeline.AppendTravelSamples(2, [0], [20], [21]);
            release.TrySetResult();
            await flush.AwaitBoundedAsync(Timeout);
            clock.Advance(Interval);
            await ready.Task.AwaitBoundedAsync(Timeout);

            var batches = received.ToArray();
            LiveSignalBatchKind[] expected = holdReset
                ? [LiveSignalBatchKind.Reset, LiveSignalBatchKind.Signals, LiveSignalBatchKind.Reset, LiveSignalBatchKind.Signals]
                : [LiveSignalBatchKind.Signals, LiveSignalBatchKind.Reset, LiveSignalBatchKind.Signals];
            Assert.Equal(expected, batches.Select(batch => batch.Kind));
            Assert.Equal(new double[] { 20 }, batches[^1].FrontTravel);
            Assert.All(batches.Zip(batches.Skip(1)), pair => Assert.True(pair.First.Revision < pair.Second.Revision));
        }
        finally { release.TrySetResult(); }
    }
}

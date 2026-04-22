using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Sufni.App.Services.LiveStreaming;

namespace Sufni.App.Tests.Services.LiveStreaming;

public class LiveGraphPipelineTests
{
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task AppendTravelSamples_FlushesBatchAtIntervalWithVelocity()
    {
        await using var pipeline = CreatePipeline();
        pipeline.Start();

        var times = BuildRampTimes(20);
        var front = BuildLinearTravel(20, startValue: 10);
        var rear = BuildLinearTravel(20, startValue: 20);

        var batchTask = WaitForBatchAsync(pipeline, batch => batch.FrontTravel.Count == 20);

        pipeline.AppendTravelSamples(times, front, rear);

        var batch = await batchTask;

        Assert.Equal(20, batch.TravelTimes.Count);
        Assert.Equal(front, batch.FrontTravel);
        Assert.Equal(rear, batch.RearTravel);
        Assert.Equal(20, batch.FrontVelocity.Count);
        Assert.All(batch.FrontVelocity, value => Assert.False(double.IsNaN(value)));
        Assert.All(batch.RearVelocity, value => Assert.False(double.IsNaN(value)));
    }

    [Fact]
    public async Task AppendTravelSamples_BelowFiveSamples_VelocityIsNaN()
    {
        await using var pipeline = CreatePipeline();
        pipeline.Start();

        var times = new[] { 0.0, 0.01, 0.02 };
        var front = new[] { 10.0, 11.0, 12.0 };
        var rear = new[] { 20.0, 21.0, 22.0 };

        var batchTask = WaitForBatchAsync(pipeline, batch => batch.FrontTravel.Count == 3);

        pipeline.AppendTravelSamples(times, front, rear);

        var batch = await batchTask;

        Assert.Equal(3, batch.FrontVelocity.Count);
        Assert.All(batch.FrontVelocity, value => Assert.True(double.IsNaN(value)));
        Assert.All(batch.RearVelocity, value => Assert.True(double.IsNaN(value)));
    }

    [Fact]
    public async Task AppendTravelSamples_LargerThanVelocityWindow_PreservesTravelAndImuData()
    {
        await using var pipeline = CreatePipeline();
        pipeline.Start();

        var sampleCount = 150;
        var times = BuildRampTimes(sampleCount);
        var front = BuildLinearTravel(sampleCount, startValue: 10);
        var rear = BuildLinearTravel(sampleCount, startValue: 20);
        var imuTimes = new[] { times[^1] };
        var imuMagnitudes = new[] { 9.81 };

        var batchTask = WaitForBatchAsync(
            pipeline,
            batch => batch.FrontTravel.Count == sampleCount
                && batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var values)
                && values.Count == 1);

        pipeline.AppendTravelSamples(times, front, rear);
        pipeline.AppendImuSamples(LiveImuLocation.Frame, imuTimes, imuMagnitudes);

        var batch = await batchTask;

        Assert.Equal(sampleCount, batch.TravelTimes.Count);
        Assert.Equal(sampleCount, batch.FrontVelocity.Count);
        Assert.Equal(sampleCount, batch.RearVelocity.Count);
        Assert.Single(batch.ImuTimes[LiveImuLocation.Frame]);
        Assert.Equal(9.81, batch.ImuMagnitudes[LiveImuLocation.Frame][0]);
        Assert.All(batch.FrontVelocity.Take(sampleCount - 127), value => Assert.True(double.IsNaN(value)));
        Assert.All(batch.FrontVelocity.Skip(sampleCount - 127), value => Assert.False(double.IsNaN(value)));
        Assert.All(batch.RearVelocity.Skip(sampleCount - 127), value => Assert.False(double.IsNaN(value)));
    }

    [Fact]
    public async Task AppendImuSamples_OnlyImu_FlushesWithEmptyTravel()
    {
        await using var pipeline = CreatePipeline();
        pipeline.Start();

        var times = new[] { 0.0 };
        var magnitudes = new[] { 1.5 };

        var batchTask = WaitForBatchAsync(
            pipeline,
            batch => batch.TravelTimes.Count == 0
                && batch.ImuTimes.TryGetValue(LiveImuLocation.Frame, out var list)
                && list.Count == 1);

        pipeline.AppendImuSamples(LiveImuLocation.Frame, times, magnitudes);

        var batch = await batchTask;

        Assert.Empty(batch.TravelTimes);
        Assert.Empty(batch.FrontTravel);
        Assert.Empty(batch.RearTravel);
        Assert.Empty(batch.FrontVelocity);
        Assert.Empty(batch.RearVelocity);
        Assert.Single(batch.ImuTimes[LiveImuLocation.Frame]);
        Assert.Equal(1.5, batch.ImuMagnitudes[LiveImuLocation.Frame][0]);
    }

    [Fact]
    public async Task AppendTravelSamples_TwoCallsBeforeFlush_MergesIntoSingleBatch()
    {
        await using var pipeline = CreatePipelineWithInterval(TimeSpan.FromMilliseconds(200));
        pipeline.Start();

        var timesA = BuildRampTimes(5);
        var frontA = BuildLinearTravel(5, startValue: 10);
        var rearA = BuildLinearTravel(5, startValue: 20);

        var timesB = BuildRampTimes(5, startOffset: 5);
        var frontB = BuildLinearTravel(5, startValue: 15);
        var rearB = BuildLinearTravel(5, startValue: 25);

        var batchTask = WaitForBatchAsync(pipeline, batch => batch.FrontTravel.Count == 10);

        pipeline.AppendTravelSamples(timesA, frontA, rearA);
        pipeline.AppendTravelSamples(timesB, frontB, rearB);

        var batch = await batchTask;

        Assert.Equal(10, batch.FrontTravel.Count);
        Assert.Equal(1L, batch.Revision);
    }

    [Fact]
    public async Task Reset_ClearsAccumulatorAndEmitsResetBatch()
    {
        await using var pipeline = CreatePipeline();
        pipeline.Start();

        var times = BuildRampTimes(10);
        var front = BuildLinearTravel(10, startValue: 10);
        var rear = BuildLinearTravel(10, startValue: 20);

        var firstBatchTask = WaitForBatchAsync(pipeline, batch => batch.FrontTravel.Count == 10);
        pipeline.AppendTravelSamples(times, front, rear);
        var firstBatch = await firstBatchTask;

        var resetBatchTask = WaitForBatchAsync(
            pipeline,
            batch => batch.TravelTimes.Count == 0
                && batch.ImuTimes.Count == 0
                && batch.Revision > firstBatch.Revision);

        pipeline.Reset();

        var resetBatch = await resetBatchTask;
        Assert.Empty(resetBatch.TravelTimes);

        var postResetTimes = new[] { 0.0, 0.01, 0.02 };
        var postResetFront = new[] { 10.0, 11.0, 12.0 };
        var postResetRear = new[] { 20.0, 21.0, 22.0 };

        var postResetBatchTask = WaitForBatchAsync(
            pipeline,
            batch => batch.FrontTravel.Count == 3 && batch.Revision > resetBatch.Revision);

        pipeline.AppendTravelSamples(postResetTimes, postResetFront, postResetRear);

        var postResetBatch = await postResetBatchTask;
        Assert.All(postResetBatch.FrontVelocity, value => Assert.True(double.IsNaN(value)));
    }

    [Fact]
    public async Task DisposeAsync_StopsLoopAndCompletesSubject()
    {
        var pipeline = CreatePipeline();
        pipeline.Start();

        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = pipeline.GraphBatches.Subscribe(
            _ => { },
            onError: _ => { },
            onCompleted: () => completed.TrySetResult());

        await pipeline.DisposeAsync();

        await completed.Task.WaitAsync(Timeout);
    }

    private static LiveGraphPipeline CreatePipeline()
    {
        return CreatePipelineWithInterval(FlushInterval);
    }

    private static LiveGraphPipeline CreatePipelineWithInterval(TimeSpan interval)
    {
        return new LiveGraphPipeline(interval, Logger.None);
    }

    private static Task<LiveGraphBatch> WaitForBatchAsync(
        ILiveGraphPipeline pipeline,
        Func<LiveGraphBatch, bool> predicate)
    {
        return pipeline.GraphBatches
            .Where(predicate)
            .FirstAsync()
            .ToTask()
            .WaitAsync(Timeout);
    }

    private static double[] BuildRampTimes(int count, int startOffset = 0)
    {
        var times = new double[count];
        for (var i = 0; i < count; i++)
        {
            times[i] = (startOffset + i) * 0.01;
        }

        return times;
    }

    private static double[] BuildLinearTravel(int count, double startValue)
    {
        var values = new double[count];
        for (var i = 0; i < count; i++)
        {
            values[i] = startValue + i;
        }

        return values;
    }
}

using Sufni.App.Tests.TestSupport.LiveDaq;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.LiveDaq.Coordinators.Capture;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Models;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.Coordinators.Capture;

public partial class LiveCaptureCoordinatorTests
{
    private static readonly TimeSpan AnalysisCadence = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(5);
    private static LiveAnalysisOptions DefaultOptions => LiveAnalysisOptions.Default(DampingSpeedCutoffs.Default);
    private static LiveAnalysisOptions OtherOptions => DefaultOptions with { VelocityMode = VelocityAverageMode.StrokePeakAveraged };

    private static Task<LiveCaptureState> NextState(ILiveCaptureCoordinator coordinator, Func<LiveCaptureState, bool> predicate) =>
        coordinator.States.Where(predicate).FirstAsync().ToTask().AwaitBoundedAsync(TestTimeout);

    [Fact]
    public async Task AnalysisDemand_DuringPressure_RunsAfterQuietPeriodWithoutAnotherFrame()
    {
        await using var coordinator = CreateCoordinator(signalPipeline: new ControllableLiveSignalPipeline(false));
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        currentState = currentState with
        {
            ClientDropCounters = LiveDaqClientDropCounters.Empty with { RawTelemetryFramesSkipped = 1 },
        };
        states.OnNext(currentState);
        var ready = NextState(coordinator, state => state.Analysis?.Options == OtherOptions);
        frames.OnNext(CreateTravelBatchFrame());
        coordinator.UpdateAnalysisOptions(OtherOptions);
        await time.WaitForTimerAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken).AwaitBoundedAsync(TestTimeout);

        time.Advance(TimeSpan.FromMilliseconds(499));
        Assert.Null(coordinator.Current.Analysis);
        time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.Equal(5, (await ready).Analysis!.Telemetry.Front.Travel.Length);
    }

    [Fact]
    public async Task RejectedMode_SelectedAgain_ComputesWithoutAnotherFrame()
    {
        var runner = new BlockingOnceBackgroundTaskRunner();
        await using var coordinator = CreateCoordinator(runner, new ControllableLiveSignalPipeline(false));
        try
        {
            await coordinator.AttachAsync(TestContext.Current.CancellationToken);
            frames.OnNext(CreateTravelBatchFrame());
            await runner.Started.Task.AwaitBoundedAsync(TestTimeout);
            coordinator.UpdateAnalysisOptions(OtherOptions);
            runner.Release();
            await time.WaitForTimerAsync(AnalysisCadence, TestContext.Current.CancellationToken).AwaitBoundedAsync(TestTimeout);
            Assert.Null(coordinator.Current.Analysis);
            var ready = NextState(coordinator, state => state.Analysis?.Options == DefaultOptions);

            coordinator.UpdateAnalysisOptions(DefaultOptions);
            time.Advance(AnalysisCadence);

            Assert.Equal(5, (await ready).Analysis!.Telemetry.Front.Travel.Length);
        }
        finally { runner.Release(); }
    }

    [Fact]
    public async Task Reset_RejectsAnInFlightResultAndAnalyzesTheNewCapture()
    {
        var runner = new BlockingOnceBackgroundTaskRunner();
        await using var coordinator = CreateCoordinator(runner, new ControllableLiveSignalPipeline(false));
        var generations = new List<long>();
        using var subscription = coordinator.States.Subscribe(state =>
        {
            if (state.Analysis is { } analysis) generations.Add(analysis.Key.ResetGeneration);
        });
        try
        {
            await coordinator.AttachAsync(TestContext.Current.CancellationToken);
            frames.OnNext(CreateTravelBatchFrame());
            await runner.Started.Task.AwaitBoundedAsync(TestTimeout);
            await coordinator.ResetAsync(TestContext.Current.CancellationToken);
            var ready = NextState(coordinator, state => state.Analysis is not null);
            frames.OnNext(CreateTravelBatchFrame(sessionHeader, 1500));
            runner.Release();

            var result = (await ready).Analysis!;

            Assert.Equal(new double[] { 150, 151, 152, 153, 154 }, result.Telemetry.Front.Travel);
            Assert.All(generations, generation => Assert.Equal(1, generation));
        }
        finally { runner.Release(); }
    }

    [Fact]
    public async Task Rollover_AcceptsInFlightAnalysisAndRetainsItsCaptureForLaterModeChanges()
    {
        var runner = new BlockingOnceBackgroundTaskRunner();
        await using var coordinator = CreateCoordinator(runner, new ControllableLiveSignalPipeline(false));
        try
        {
            await coordinator.AttachAsync(TestContext.Current.CancellationToken);
            var ready = NextState(coordinator, state => state.Analysis is not null);
            frames.OnNext(CreateTravelBatchFrame());
            await runner.Started.Task.AwaitBoundedAsync(TestTimeout);
            PublishSharedState(CreateRolloverSessionHeader(), isClosed: false);
            runner.Release();
            var original = await ready;
            Assert.Equal(LiveCapturePhase.AwaitingReset, original.Controls.Phase);
            Assert.Equal(sessionHeader.SessionId, original.Controls.SessionHeader!.SessionId);
            await time.WaitForTimerAsync(AnalysisCadence, TestContext.Current.CancellationToken).AwaitBoundedAsync(TestTimeout);
            time.Advance(AnalysisCadence);
            var changed = NextState(coordinator, state => state.Analysis?.Options == OtherOptions);
            coordinator.UpdateAnalysisOptions(OtherOptions);

            var next = await changed;

            Assert.Equal(original.CaptureRevision, next.CaptureRevision);
            Assert.Equal(original.Analysis!.Telemetry.Front.Travel, next.Analysis!.Telemetry.Front.Travel);
            sessionPresentationService.Received(2).CalculateDampingPercentages(Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(), Arg.Any<VelocityAverageMode>(), Arg.Any<DampingSpeedCutoffs?>());
        }
        finally { runner.Release(); }
    }

    [Fact]
    public async Task FailedAnalysis_SettlesUntilItsInputsChange()
    {
        sessionPresentationService.CalculateDampingPercentages(Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(), Arg.Any<VelocityAverageMode>(), Arg.Any<DampingSpeedCutoffs?>())
            .Returns(_ => throw new InvalidOperationException("calculation failed"));
        await using var coordinator = CreateCoordinator(signalPipeline: new ControllableLiveSignalPipeline(false));
        await coordinator.AttachAsync(TestContext.Current.CancellationToken);
        var failed = NextState(coordinator, state => state.AnalysisFailure is not null);
        frames.OnNext(CreateTravelBatchFrame());
        await failed;
        await time.WaitForTimerAsync(AnalysisCadence, TestContext.Current.CancellationToken).AwaitBoundedAsync(TestTimeout);
        time.Advance(AnalysisCadence);
        var retried = NextState(coordinator, state => state.Analysis?.Options == OtherOptions);
        sessionPresentationService.CalculateDampingPercentages(Arg.Any<TelemetryData>(),
                Arg.Any<TelemetryTimeRange?>(), Arg.Any<VelocityAverageMode>(), Arg.Any<DampingSpeedCutoffs?>())
            .Returns(SessionDampingPercentages.Empty);

        coordinator.UpdateAnalysisOptions(OtherOptions);
        await retried;

        sessionPresentationService.Received(2).CalculateDampingPercentages(Arg.Any<TelemetryData>(),
            Arg.Any<TelemetryTimeRange?>(), Arg.Any<VelocityAverageMode>(), Arg.Any<DampingSpeedCutoffs?>());
    }
    [Fact]
    public async Task DisplayReset_DuringFlush_OnlyNewCaptureFollowsReset()
    {
        var signalClock = new SignalingFakeTimeProvider();
        var pipeline = new ControllableLiveSignalPipeline(false,
            new LiveSignalPipeline(UiCadence, Serilog.Core.Logger.None, signalClock));
        await using var coordinator = CreateCoordinator(signalPipeline: pipeline, sendOptions: false);
        var received = new System.Collections.Concurrent.ConcurrentQueue<LiveSignalBatch>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = coordinator.SignalBatches.Subscribe(batch =>
        {
            received.Enqueue(batch);
            if (entered.TrySetResult()) release.Task.AwaitBoundedAsync(TestTimeout).GetAwaiter().GetResult();
            if (batch.FrontTravel.Contains(150)) ready.TrySetResult();
        });
        try
        {
            await coordinator.AttachAsync(TestContext.Current.CancellationToken);
            await signalClock.WaitForTimerAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TestTimeout);
            frames.OnNext(CreateTravelBatchFrame());
            await pipeline.WaitForTravelAppendAsync();
            var flush = Task.Run(() => signalClock.Advance(UiCadence), TestContext.Current.CancellationToken);
            await entered.Task.AwaitBoundedAsync(TestTimeout);
            frames.OnNext(CreateTravelBatchFrame(firstMonotonicUs: sessionHeader.SessionStartMonotonicUs + 25_000));
            await pipeline.WaitForTravelAppendAsync();
            await coordinator.ResetAsync(TestContext.Current.CancellationToken);
            frames.OnNext(CreateTravelBatchFrame(sessionHeader, 1500));
            await pipeline.WaitForTravelAppendAsync();
            release.TrySetResult();
            await flush.AwaitBoundedAsync(TestTimeout);
            signalClock.Advance(UiCadence);
            await ready.Task.AwaitBoundedAsync(TestTimeout);

            var batches = received.ToArray();
            Assert.Equal(new[] { LiveSignalBatchKind.Signals, LiveSignalBatchKind.Reset, LiveSignalBatchKind.Signals },
                batches.Select(batch => batch.Kind));
            Assert.Equal(new double[] { 150, 151, 152, 153, 154 }, batches[2].FrontTravel);
        }
        finally { release.TrySetResult(); }
    }

}

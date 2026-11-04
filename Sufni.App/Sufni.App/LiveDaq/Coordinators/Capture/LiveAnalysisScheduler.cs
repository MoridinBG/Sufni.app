using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Services;
using Sufni.App.Shared.Plots;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

public sealed record LiveAnalysisKey(long ResetGeneration, long CaptureRevision, LiveAnalysisOptions Options);

internal sealed record LiveAnalysisJob(
    LiveAnalysisKey Key, LiveCaptureContent Content, BikeData BikeData);

internal abstract record LiveAnalysisCompletion(LiveAnalysisKey Key)
{
    public sealed record Computed(LiveAnalysisKey Key, LiveSessionAnalysis Analysis)
        : LiveAnalysisCompletion(Key);
    public sealed record Failed(LiveAnalysisKey Key, LiveAnalysisFailure Failure) : LiveAnalysisCompletion(Key);
}

internal sealed class LiveAnalysisScheduler : IAsyncDisposable
{
    private static readonly TimeSpan PressureQuietPeriod = TimeSpan.FromMilliseconds(500);
    private readonly System.Threading.Lock gate = new();
    private readonly Channel<bool> wakes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        AllowSynchronousContinuations = false,
    });
    private readonly CancellationTokenSource lifetime = new();
    private readonly TimeProvider clock;
    private readonly IBackgroundTaskRunner runner;
    private readonly ISessionPresentationService presentation;
    private readonly Func<LiveAnalysisJob?> getJob;
    private readonly Action<LiveAnalysisCompletion> complete;
    private readonly string identityKey;
    private readonly Task loop;
    private ITimer? timer;
    private DateTimeOffset nextRunAt = DateTimeOffset.MinValue;
    private DateTimeOffset pressureAt = DateTimeOffset.MinValue;
    private ulong lastDropTotal;

    public LiveAnalysisScheduler(TimeProvider clock, IBackgroundTaskRunner runner,
        ISessionPresentationService presentation, Func<LiveAnalysisJob?> getJob,
        Action<LiveAnalysisCompletion> complete, string identityKey)
    {
        this.clock = clock;
        this.runner = runner;
        this.presentation = presentation;
        this.getJob = getJob;
        this.complete = complete;
        this.identityKey = identityKey;
        loop = Task.Run(RunAsync);
    }

    public void Wake() => wakes.Writer.TryWrite(true);

    public void ObserveDrops(LiveDaqClientDropCounters counters)
    {
        var total = counters.RawTelemetryFramesSkipped + counters.ParsedTelemetryFramesDropped +
            counters.SubscriberFramesDropped + counters.SignalBatchesCoalesced + counters.SignalSamplesDiscarded;
        lock (gate)
        {
            if (total > lastDropTotal) pressureAt = clock.GetUtcNow();
            lastDropTotal = total;
        }
        Wake();
    }

    public void ReportPressure()
    {
        lock (gate) pressureAt = clock.GetUtcNow();
        Wake();
    }

    public void Reset()
    {
        lock (gate)
        {
            nextRunAt = DateTimeOffset.MinValue;
            pressureAt = DateTimeOffset.MinValue;
            timer?.Dispose();
            timer = null;
        }
        Wake();
    }

    public async ValueTask DisposeAsync()
    {
        lifetime.Cancel();
        lock (gate)
        {
            timer?.Dispose();
            timer = null;
        }
        wakes.Writer.TryComplete();
        await loop;
        lifetime.Dispose();
    }

    private async Task RunAsync()
    {
        try
        {
            await foreach (var _ in wakes.Reader.ReadAllAsync(lifetime.Token))
            {
                DateTimeOffset previousNextRun;
                DateTimeOffset reservedNextRun;
                lock (gate)
                {
                    if (lifetime.IsCancellationRequested) return;
                    timer?.Dispose();
                    timer = null;
                    var now = clock.GetUtcNow();
                    var due = nextRunAt > pressureAt + PressureQuietPeriod
                        ? nextRunAt : pressureAt + PressureQuietPeriod;
                    if (due > now)
                    {
                        timer = clock.CreateTimer(_ => Wake(), null, due - now, Timeout.InfiniteTimeSpan);
                        continue;
                    }
                    previousNextRun = nextRunAt;
                    reservedNextRun = now.AddMilliseconds(PlotSettings.LiveAnalysisRefreshIntervalMs);
                    nextRunAt = reservedNextRun;
                }

                var job = getJob();
                if (job is null)
                {
                    lock (gate)
                        if (nextRunAt == reservedNextRun) nextRunAt = previousNextRun;
                    continue;
                }

                LiveAnalysisCompletion result;
                try
                {
                    result = await runner.RunAsync<LiveAnalysisCompletion>(() =>
                    {
                        var telemetry = TelemetryData.FromLiveCapture(
                            LiveCaptureMaterializer.Materialize(job.Content, job.BikeData));
                        var percentages = presentation.CalculateDampingPercentages(telemetry,
                            velocityAverageMode: job.Key.Options.VelocityMode,
                            dampingSpeedCutoffs: job.Key.Options.DampingCutoffs);
                        return new LiveAnalysisCompletion.Computed(job.Key,
                            LiveSessionAnalysis.Compute(job.Key, telemetry, percentages));
                    }, lifetime.Token);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
                catch (Exception error)
                {
                    Log.ForContext<LiveAnalysisScheduler>().Warning(error,
                        "Live session analysis recompute failed for {IdentityKey}", identityKey);
                    result = new LiveAnalysisCompletion.Failed(job.Key,
                        new LiveAnalysisFailure(LiveAnalysisFailureReason.Calculation, error.ToString()));
                }
                complete(result);
                Wake();
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
    }
}

using System.Threading.Channels;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.TestSupport.LiveDaq;

internal sealed class ControllableLiveSignalPipeline(bool blockFirstTravel = true, ILiveSignalPipeline? inner = null) : ILiveSignalPipeline
{
    private readonly Subject<LiveSignalBatch> signalBatches = new();
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<bool> imuAppends = Channel.CreateUnbounded<bool>();
    private readonly Channel<bool> travelAppends = Channel.CreateUnbounded<bool>();
    private int shouldBlock = blockFirstTravel ? 1 : 0;

    public TaskCompletionSource AppendStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Action? OnReset { get; set; }

    public IObservable<LiveSignalBatch> SignalBatches => inner?.SignalBatches ?? signalBatches.AsObservable();

    public void Start() => inner?.Start();

    public async Task WaitForTravelAppendAsync() =>
        await travelAppends.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().AwaitBoundedAsync(TimeSpan.FromSeconds(5));

    public async Task WaitForImuAppendAsync() =>
        await imuAppends.Reader.ReadAsync(TestContext.Current.CancellationToken).AsTask().AwaitBoundedAsync(TimeSpan.FromSeconds(5));

    public void AppendTravelSamples(long epoch, ReadOnlySpan<double> times, ReadOnlySpan<double> frontTravel, ReadOnlySpan<double> rearTravel)
    {
        if (Interlocked.Exchange(ref shouldBlock, 0) == 1)
        {
            AppendStarted.TrySetResult();
            release.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }

        if (inner is not null) inner.AppendTravelSamples(epoch, times, frontTravel, rearTravel);
        else signalBatches.OnNext(LiveSignalBatch.Empty with
        {
            TravelTimes = times.ToArray(),
            FrontTravel = frontTravel.ToArray(),
            RearTravel = rearTravel.ToArray(),
        });
        travelAppends.Writer.TryWrite(true);
    }

    public void AppendImuSamples(long epoch, LiveImuLocation location, ReadOnlySpan<double> times, ReadOnlySpan<double> vibrationRms)
    {
        inner?.AppendImuSamples(epoch, location, times, vibrationRms);
        imuAppends.Writer.TryWrite(true);
    }

    public void AppendFramePitchRollSamples(long epoch, ReadOnlySpan<double> times, ReadOnlySpan<double> pitchDegrees, ReadOnlySpan<double> rollDegrees) =>
        inner?.AppendFramePitchRollSamples(epoch, times, pitchDegrees, rollDegrees);


    public void BeginEpoch(long epoch)
    {
        OnReset?.Invoke();
        inner?.BeginEpoch(epoch);
    }

    public void Release()
    {
        release.TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        Release();
        signalBatches.OnCompleted();
        signalBatches.Dispose();
        if (inner is not null) await inner.DisposeAsync();
    }
}

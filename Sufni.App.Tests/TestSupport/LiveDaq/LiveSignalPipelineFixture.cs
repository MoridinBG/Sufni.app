using Serilog.Core;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.TestSupport.LiveDaq;

internal sealed class LiveSignalPipelineFixture
{
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(33);
    private readonly SignalingFakeTimeProvider clock = new();

    public ControllableLiveSignalPipeline Pipeline { get; }

    public LiveSignalPipelineFixture() =>
        Pipeline = new ControllableLiveSignalPipeline(false, new LiveSignalPipeline(Interval, Logger.None, clock));

    public Task WaitForStartAsync() =>
        clock.WaitForTimerAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));

    public void Flush() => clock.Advance(Interval);

    public async Task FlushTravelAsync()
    {
        await Pipeline.WaitForTravelAppendAsync();
        Flush();
    }

    public async Task FlushImuAsync()
    {
        await Pipeline.WaitForImuAppendAsync();
        Flush();
    }
}

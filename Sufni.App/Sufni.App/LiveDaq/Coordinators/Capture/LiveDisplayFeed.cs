using System;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

internal sealed class LiveDisplayFeed(ILiveSignalPipeline signalPipeline) : IAsyncDisposable
{
    private const int DisplayUpdateQueueCapacity = 8;
    private readonly System.Threading.Lock displayQueueGate = new();
    private readonly CancellationTokenSource lifetime = new();
    private int queuedDisplayUpdates;
    private Task? loop;
    private readonly Channel<LiveDisplayUpdate> displayUpdates = Channel.CreateBounded<LiveDisplayUpdate>(
        new BoundedChannelOptions(DisplayUpdateQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });

    public void Start()
    {
        signalPipeline.Start();
        loop ??= Task.Run(() => RunDisplayLoopAsync(lifetime.Token));
    }

    public async Task DrainAsync()
    {
        lifetime.Cancel();
        displayUpdates.Writer.TryComplete();
        if (loop is not null) await loop;
    }

    public async ValueTask DisposeAsync()
    {
        await signalPipeline.DisposeAsync();
        lifetime.Dispose();
    }

    public bool Offer(LiveDisplayUpdate update)
    {
        var droppedOldest = false;
        lock (displayQueueGate)
        {
            droppedOldest = queuedDisplayUpdates >= DisplayUpdateQueueCapacity;
            if (!displayUpdates.Writer.TryWrite(update))
            {
                return false;
            }

            if (droppedOldest)
            {
                queuedDisplayUpdates = DisplayUpdateQueueCapacity;
            }
            else
            {
                queuedDisplayUpdates++;
            }
        }

        return droppedOldest;
    }

    private async Task RunDisplayLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var update in displayUpdates.Reader.ReadAllAsync(cancellationToken))
            {
                lock (displayQueueGate)
                {
                    queuedDisplayUpdates = Math.Max(0, queuedDisplayUpdates - 1);
                }

                switch (update)
                {
                    case LiveDisplayUpdate.Travel travel:
                        signalPipeline.AppendTravelSamples(update.Epoch, travel.Times, travel.FrontTravel, travel.RearTravel);
                        break;

                    case LiveDisplayUpdate.Imu imu:
                        foreach (var entry in imu.Times)
                        {
                            if (!imu.VibrationRms.TryGetValue(entry.Key, out var vibrationRms))
                            {
                                continue;
                            }

                            var times = entry.Value as double[] ?? entry.Value.ToArray();
                            var vibrationValues = vibrationRms as double[] ?? vibrationRms.ToArray();
                            signalPipeline.AppendImuSamples(update.Epoch, entry.Key, times, vibrationValues);
                        }

                        if (imu.FramePitchRoll is { } pitchRoll)
                        {
                            signalPipeline.AppendFramePitchRollSamples(update.Epoch,
                                pitchRoll.Times,
                                pitchRoll.PitchDegrees,
                                pitchRoll.RollDegrees);
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

}

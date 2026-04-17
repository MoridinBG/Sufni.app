using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.Telemetry;

namespace Sufni.App.Services.LiveStreaming;

internal sealed class LiveGraphPipeline : ILiveGraphPipeline
{
    private const int MaxVelocityWindowSamples = 127;

    private readonly TimeSpan flushInterval;
    private readonly ILogger logger;
    private readonly object gate = new();
    private readonly Subject<LiveGraphBatch> graphBatchesSubject = new();
    private readonly PendingGraphBatch pendingGraphBatch = new();
    private readonly SlidingWindowBuffer<double> recentTravelTimes = new(MaxVelocityWindowSamples);
    private readonly SlidingWindowBuffer<double> recentFrontTravel = new(MaxVelocityWindowSamples);
    private readonly SlidingWindowBuffer<double> recentRearTravel = new(MaxVelocityWindowSamples);

    // Flush-loop-owned. Never read or written from Append* or Reset paths.
    private SavitzkyGolay? cachedVelocityFilter;
    private int cachedVelocityFilterWindow;

    private CancellationTokenSource? flushCts;
    private Task? flushLoopTask;
    private long graphRevision;
    private bool isStarted;
    private bool isDisposed;

    public LiveGraphPipeline(TimeSpan flushInterval, ILogger logger)
    {
        this.flushInterval = flushInterval;
        this.logger = logger;
    }

    public IObservable<LiveGraphBatch> GraphBatches => graphBatchesSubject.AsObservable();

    public void Start()
    {
        lock (gate)
        {
            if (isDisposed || isStarted)
            {
                return;
            }

            isStarted = true;
            flushCts = new CancellationTokenSource();
            flushLoopTask = Task.Run(() => RunFlushLoopAsync(flushCts.Token));
        }
    }

    public void AppendTravelSamples(ReadOnlySpan<double> times, ReadOnlySpan<double> frontTravel, ReadOnlySpan<double> rearTravel)
    {
        if (times.Length == 0)
        {
            return;
        }

        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            for (var i = 0; i < times.Length; i++)
            {
                pendingGraphBatch.TravelTimes.Add(times[i]);
                pendingGraphBatch.FrontTravel.Add(frontTravel[i]);
                pendingGraphBatch.RearTravel.Add(rearTravel[i]);
                recentTravelTimes.Append(times[i]);
                recentFrontTravel.Append(frontTravel[i]);
                recentRearTravel.Append(rearTravel[i]);
            }
        }
    }

    public void AppendImuSamples(LiveImuLocation location, ReadOnlySpan<double> times, ReadOnlySpan<double> magnitudes)
    {
        if (times.Length == 0)
        {
            return;
        }

        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            if (!pendingGraphBatch.ImuTimes.TryGetValue(location, out var imuTimes))
            {
                imuTimes = new List<double>();
                pendingGraphBatch.ImuTimes[location] = imuTimes;
            }

            if (!pendingGraphBatch.ImuMagnitudes.TryGetValue(location, out var imuMagnitudes))
            {
                imuMagnitudes = new List<double>();
                pendingGraphBatch.ImuMagnitudes[location] = imuMagnitudes;
            }

            for (var i = 0; i < times.Length; i++)
            {
                imuTimes.Add(times[i]);
                imuMagnitudes.Add(magnitudes[i]);
            }
        }
    }

    public void Reset()
    {
        long resetRevision;
        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            pendingGraphBatch.Clear();
            recentTravelTimes.Clear();
            recentFrontTravel.Clear();
            recentRearTravel.Clear();
            graphRevision++;
            resetRevision = graphRevision;
        }

        graphBatchesSubject.OnNext(LiveGraphBatch.Empty with { Revision = resetRevision });
    }

    public async ValueTask DisposeAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;

        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            cts = flushCts;
            loop = flushLoopTask;
            flushCts = null;
            flushLoopTask = null;
        }

        cts?.Cancel();

        if (loop is not null)
        {
            try
            {
                await loop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        cts?.Dispose();
        graphBatchesSubject.OnCompleted();
        graphBatchesSubject.Dispose();
    }

    private async Task RunFlushLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(flushInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    FlushPendingGraphBatch();
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.Warning(ex, "Live graph flush failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void FlushPendingGraphBatch()
    {
        double[] travelTimesArr;
        double[] frontTravelArr;
        double[] rearTravelArr;
        Dictionary<LiveImuLocation, IReadOnlyList<double>> imuTimesDict;
        Dictionary<LiveImuLocation, IReadOnlyList<double>> imuMagnitudesDict;
        double[]? velocityTimesSnap = null;
        double[]? velocityFrontSnap = null;
        double[]? velocityRearSnap = null;
        int batchCount;
        long batchRevision;

        lock (gate)
        {
            if (!pendingGraphBatch.HasContent)
            {
                return;
            }

            batchCount = pendingGraphBatch.TravelTimes.Count;
            travelTimesArr = pendingGraphBatch.TravelTimes.ToArray();
            frontTravelArr = pendingGraphBatch.FrontTravel.ToArray();
            rearTravelArr = pendingGraphBatch.RearTravel.ToArray();

            imuTimesDict = new Dictionary<LiveImuLocation, IReadOnlyList<double>>(pendingGraphBatch.ImuTimes.Count);
            imuMagnitudesDict = new Dictionary<LiveImuLocation, IReadOnlyList<double>>(pendingGraphBatch.ImuMagnitudes.Count);
            foreach (var entry in pendingGraphBatch.ImuTimes)
            {
                imuTimesDict[entry.Key] = entry.Value.ToArray();
            }

            foreach (var entry in pendingGraphBatch.ImuMagnitudes)
            {
                imuMagnitudesDict[entry.Key] = entry.Value.ToArray();
            }

            if (batchCount > 0)
            {
                velocityTimesSnap = recentTravelTimes.ToArray();
                velocityFrontSnap = recentFrontTravel.ToArray();
                velocityRearSnap = recentRearTravel.ToArray();
            }

            graphRevision++;
            batchRevision = graphRevision;
            pendingGraphBatch.Clear();
        }

        double[] frontVelocity;
        double[] rearVelocity;
        if (batchCount > 0)
        {
            frontVelocity = ComputeVelocityAppend(velocityTimesSnap!, velocityFrontSnap!, batchCount);
            rearVelocity = ComputeVelocityAppend(velocityTimesSnap!, velocityRearSnap!, batchCount);
        }
        else
        {
            frontVelocity = [];
            rearVelocity = [];
        }

        var batch = new LiveGraphBatch(
            Revision: batchRevision,
            TravelTimes: travelTimesArr,
            FrontTravel: frontTravelArr,
            RearTravel: rearTravelArr,
            VelocityTimes: travelTimesArr,
            FrontVelocity: frontVelocity,
            RearVelocity: rearVelocity,
            ImuTimes: imuTimesDict,
            ImuMagnitudes: imuMagnitudesDict);

        graphBatchesSubject.OnNext(batch);
    }

    private double[] ComputeVelocityAppend(IReadOnlyList<double> times, IReadOnlyList<double> travel, int batchCount)
    {
        if (times.Count < 5 || travel.Any(double.IsNaN))
        {
            return Enumerable.Repeat(double.NaN, batchCount).ToArray();
        }

        var filterWindow = Math.Min(51, times.Count);
        if (filterWindow % 2 == 0)
        {
            filterWindow--;
        }

        if (cachedVelocityFilter is null || cachedVelocityFilterWindow != filterWindow)
        {
            cachedVelocityFilter = SavitzkyGolay.Create(filterWindow, 1, 3);
            cachedVelocityFilterWindow = filterWindow;
        }

        var velocities = cachedVelocityFilter.Process(travel.ToArray(), times.ToArray());
        return velocities[^batchCount..];
    }

    private sealed class PendingGraphBatch
    {
        public List<double> TravelTimes { get; } = new();
        public List<double> FrontTravel { get; } = new();
        public List<double> RearTravel { get; } = new();
        public Dictionary<LiveImuLocation, List<double>> ImuTimes { get; } = new();
        public Dictionary<LiveImuLocation, List<double>> ImuMagnitudes { get; } = new();

        public bool HasContent
        {
            get
            {
                if (TravelTimes.Count > 0)
                {
                    return true;
                }

                foreach (var entry in ImuTimes)
                {
                    if (entry.Value.Count > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public void Clear()
        {
            TravelTimes.Clear();
            FrontTravel.Clear();
            RearTravel.Clear();
            foreach (var entry in ImuTimes)
            {
                entry.Value.Clear();
            }

            foreach (var entry in ImuMagnitudes)
            {
                entry.Value.Clear();
            }
        }
    }
}

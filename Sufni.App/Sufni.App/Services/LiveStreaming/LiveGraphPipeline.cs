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
    private const int MaxVelocityContextMilliseconds = 127;
    private const int VelocityFilterWindowMilliseconds = 51;
    private const int MinimumVelocityFilterSamples = 5;

    private readonly TimeSpan flushInterval;
    private readonly ILogger logger;
    private readonly object gate = new();
    private readonly Subject<LiveGraphBatch> graphBatchesSubject = new();
    private PendingGraphBatch pendingGraphBatch = new();
    private readonly List<double> recentTravelTimes = new();
    private readonly List<double> recentFrontTravel = new();
    private readonly List<double> recentRearTravel = new();

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
                recentTravelTimes.Add(times[i]);
                recentFrontTravel.Add(frontTravel[i]);
                recentRearTravel.Add(rearTravel[i]);
            }

            TrimRecentTravelWindowLocked();
        }
    }

    public void AppendImuSamples(LiveImuLocation location, ReadOnlySpan<double> times, ReadOnlySpan<double> vibrationRms)
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

            if (!pendingGraphBatch.ImuVibrationRms.TryGetValue(location, out var imuVibrationRms))
            {
                imuVibrationRms = new List<double>();
                pendingGraphBatch.ImuVibrationRms[location] = imuVibrationRms;
            }

            for (var i = 0; i < times.Length; i++)
            {
                imuTimes.Add(times[i]);
                imuVibrationRms.Add(vibrationRms[i]);
            }
        }
    }

    public void AppendFramePitchRollSamples(ReadOnlySpan<double> times, ReadOnlySpan<double> pitchDegrees, ReadOnlySpan<double> rollDegrees)
    {
        if (times.Length == 0)
        {
            return;
        }

        var count = Math.Min(times.Length, Math.Min(pitchDegrees.Length, rollDegrees.Length));
        if (count == 0)
        {
            return;
        }

        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            for (var i = 0; i < count; i++)
            {
                pendingGraphBatch.FramePitchRollTimes.Add(times[i]);
                pendingGraphBatch.FramePitchDegrees.Add(pitchDegrees[i]);
                pendingGraphBatch.FrameRollDegrees.Add(rollDegrees[i]);
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
        Dictionary<LiveImuLocation, IReadOnlyList<double>> imuVibrationRmsDict;
        double[] framePitchRollTimesArr;
        double[] framePitchArr;
        double[] frameRollArr;
        double[]? velocityTimesSnap = null;
        double[]? velocityFrontSnap = null;
        double[]? velocityRearSnap = null;
        int batchCount;
        long batchRevision;
        PendingGraphBatch batchToFlush;

        lock (gate)
        {
            if (!pendingGraphBatch.HasContent)
            {
                return;
            }

            batchToFlush = pendingGraphBatch;
            pendingGraphBatch = new PendingGraphBatch();

            batchCount = batchToFlush.TravelTimes.Count;
            travelTimesArr = batchToFlush.TravelTimes.ToArray();
            frontTravelArr = batchToFlush.FrontTravel.ToArray();
            rearTravelArr = batchToFlush.RearTravel.ToArray();

            imuTimesDict = new Dictionary<LiveImuLocation, IReadOnlyList<double>>(batchToFlush.ImuTimes.Count);
            imuVibrationRmsDict = new Dictionary<LiveImuLocation, IReadOnlyList<double>>(batchToFlush.ImuVibrationRms.Count);
            foreach (var entry in batchToFlush.ImuTimes)
            {
                imuTimesDict[entry.Key] = entry.Value.ToArray();
            }

            foreach (var entry in batchToFlush.ImuVibrationRms)
            {
                imuVibrationRmsDict[entry.Key] = entry.Value.ToArray();
            }

            framePitchRollTimesArr = batchToFlush.FramePitchRollTimes.ToArray();
            framePitchArr = batchToFlush.FramePitchDegrees.ToArray();
            frameRollArr = batchToFlush.FrameRollDegrees.ToArray();

            if (batchCount > 0)
            {
                velocityTimesSnap = recentTravelTimes.ToArray();
                velocityFrontSnap = recentFrontTravel.ToArray();
                velocityRearSnap = recentRearTravel.ToArray();
            }

            graphRevision++;
            batchRevision = graphRevision;
        }

        try
        {
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
                ImuVibrationRms: imuVibrationRmsDict,
                FramePitchRollTimes: framePitchRollTimesArr,
                FramePitchDegrees: framePitchArr,
                FrameRollDegrees: frameRollArr);

            graphBatchesSubject.OnNext(batch);
        }
        catch
        {
            lock (gate)
            {
                batchToFlush.AppendFrom(pendingGraphBatch);
                pendingGraphBatch = batchToFlush;
            }

            throw;
        }
    }

    private double[] ComputeVelocityAppend(IReadOnlyList<double> times, IReadOnlyList<double> travel, int batchCount)
    {
        if (times.Count < MinimumVelocityFilterSamples || travel.Any(double.IsNaN))
        {
            return Enumerable.Repeat(double.NaN, batchCount).ToArray();
        }

        var filterWindow = CalculateVelocityFilterWindow(times);

        if (cachedVelocityFilter is null || cachedVelocityFilterWindow != filterWindow)
        {
            cachedVelocityFilter = SavitzkyGolay.Create(filterWindow, 1, 3);
            cachedVelocityFilterWindow = filterWindow;
        }

        var velocities = cachedVelocityFilter.Process(travel.ToArray(), times.ToArray());
        if (velocities.Length >= batchCount)
        {
            return velocities[^batchCount..];
        }

        var result = Enumerable.Repeat(double.NaN, batchCount).ToArray();
        Array.Copy(
            velocities,
            sourceIndex: 0,
            result,
            destinationIndex: batchCount - velocities.Length,
            length: velocities.Length);
        return result;
    }

    private void TrimRecentTravelWindowLocked()
    {
        if (recentTravelTimes.Count <= MinimumVelocityFilterSamples)
        {
            return;
        }

        var latest = recentTravelTimes[^1];
        var removeCount = 0;
        var maxContextSeconds = MaxVelocityContextMilliseconds / 1000.0;
        while (recentTravelTimes.Count - removeCount > MinimumVelocityFilterSamples &&
               latest - recentTravelTimes[removeCount] >= maxContextSeconds)
        {
            removeCount++;
        }

        if (removeCount == 0)
        {
            return;
        }

        recentTravelTimes.RemoveRange(0, removeCount);
        recentFrontTravel.RemoveRange(0, removeCount);
        recentRearTravel.RemoveRange(0, removeCount);
    }

    private static int CalculateVelocityFilterWindow(IReadOnlyList<double> times)
    {
        var samplePeriodSeconds = InferSamplePeriodSeconds(times);
        if (!double.IsFinite(samplePeriodSeconds) || samplePeriodSeconds <= 0)
        {
            return MinimumVelocityFilterSamples;
        }

        var filterWindow = Math.Max(
            MinimumVelocityFilterSamples,
            (int)Math.Round(VelocityFilterWindowMilliseconds / 1000.0 / samplePeriodSeconds));
        if (filterWindow % 2 == 0)
        {
            filterWindow++;
        }

        filterWindow = Math.Min(filterWindow, times.Count);
        if (filterWindow % 2 == 0)
        {
            filterWindow--;
        }

        return Math.Max(MinimumVelocityFilterSamples, filterWindow);
    }

    private static double InferSamplePeriodSeconds(IReadOnlyList<double> times)
    {
        if (times.Count < 2)
        {
            return double.NaN;
        }

        var duration = times[^1] - times[0];
        return duration > 0
            ? duration / (times.Count - 1)
            : double.NaN;
    }

    private sealed class PendingGraphBatch
    {
        public List<double> TravelTimes { get; } = new();
        public List<double> FrontTravel { get; } = new();
        public List<double> RearTravel { get; } = new();
        public Dictionary<LiveImuLocation, List<double>> ImuTimes { get; } = new();
        public Dictionary<LiveImuLocation, List<double>> ImuVibrationRms { get; } = new();
        public List<double> FramePitchRollTimes { get; } = new();
        public List<double> FramePitchDegrees { get; } = new();
        public List<double> FrameRollDegrees { get; } = new();

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

                foreach (var entry in ImuVibrationRms)
                {
                    if (entry.Value.Count > 0)
                    {
                        return true;
                    }
                }

                return FramePitchRollTimes.Count > 0 ||
                    FramePitchDegrees.Count > 0 ||
                    FrameRollDegrees.Count > 0;
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

            foreach (var entry in ImuVibrationRms)
            {
                entry.Value.Clear();
            }

            FramePitchRollTimes.Clear();
            FramePitchDegrees.Clear();
            FrameRollDegrees.Clear();
        }

        public void AppendFrom(PendingGraphBatch other)
        {
            TravelTimes.AddRange(other.TravelTimes);
            FrontTravel.AddRange(other.FrontTravel);
            RearTravel.AddRange(other.RearTravel);

            foreach (var entry in other.ImuTimes)
            {
                if (!ImuTimes.TryGetValue(entry.Key, out var values))
                {
                    values = new List<double>();
                    ImuTimes[entry.Key] = values;
                }

                values.AddRange(entry.Value);
            }

            foreach (var entry in other.ImuVibrationRms)
            {
                if (!ImuVibrationRms.TryGetValue(entry.Key, out var values))
                {
                    values = new List<double>();
                    ImuVibrationRms[entry.Key] = values;
                }

                values.AddRange(entry.Value);
            }

            FramePitchRollTimes.AddRange(other.FramePitchRollTimes);
            FramePitchDegrees.AddRange(other.FramePitchDegrees);
            FrameRollDegrees.AddRange(other.FrameRollDegrees);
        }
    }
}

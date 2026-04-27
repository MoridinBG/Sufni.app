using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.SessionGraphs;
using Sufni.App.Services;
using Sufni.Telemetry;
using Serilog;

namespace Sufni.App.Services.LiveStreaming;

internal sealed class LiveSessionServiceFactory(
    ISessionPresentationService sessionPresentationService,
    IBackgroundTaskRunner backgroundTaskRunner,
    ILiveGraphPipelineFactory liveGraphPipelineFactory) : ILiveSessionServiceFactory
{
    public ILiveSessionService Create(LiveDaqSessionContext context, ILiveDaqSharedStream sharedStream)
    {
        return new LiveSessionService(
            context,
            sharedStream,
            sessionPresentationService,
            backgroundTaskRunner,
            liveGraphPipelineFactory.Create());
    }
}

internal sealed class LiveSessionService : ILiveSessionService
{
    private const int MeasurementChunkSize = 4096;
    private const int ImuChunkSize = 2048;
    private const int GpsChunkSize = 256;
    private const int DisplayUpdateQueueCapacity = 8;
    private static readonly TimeSpan StatisticsPressureQuietPeriod = TimeSpan.FromMilliseconds(500);

    private static readonly ILogger logger = Log.ForContext<LiveSessionService>();

    private readonly record struct LiveCaptureSnapshot(
        Metadata Metadata,
        LiveSessionHeader? SessionHeader,
        ChunkedBufferSnapshot<ushort> FrontMeasurements,
        ChunkedBufferSnapshot<ushort> RearMeasurements,
        ChunkedBufferSnapshot<ImuRecord> ImuRecords,
        ChunkedBufferSnapshot<GpsRecord> GpsRecords);

    private abstract record LiveDisplayUpdate(long Epoch)
    {
        public abstract int SampleCount { get; }

        public sealed record Travel(
            long Epoch,
            double[] Times,
            double[] FrontTravel,
            double[] RearTravel) : LiveDisplayUpdate(Epoch)
        {
            public override int SampleCount => Times.Length;
        }

        public sealed record Imu(
            long Epoch,
            IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> Times,
            IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> Magnitudes) : LiveDisplayUpdate(Epoch)
        {
            public override int SampleCount => Times.Values.Sum(series => series.Count);
        }
    }

    private readonly LiveDaqSessionContext context;
    private readonly ILiveDaqSharedStream sharedStream;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ILiveGraphPipeline graphPipeline;
    private readonly object gate = new();
    private readonly object displayQueueGate = new();
    private readonly BehaviorSubject<LiveSessionPresentationSnapshot> snapshotsSubject = new(LiveSessionPresentationSnapshot.Empty);
    private readonly CancellationTokenSource disposalCts = new();
    private readonly Channel<LiveDisplayUpdate> displayUpdates = Channel.CreateBounded<LiveDisplayUpdate>(
        new BoundedChannelOptions(DisplayUpdateQueueCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest,
            AllowSynchronousContinuations = false,
        });

    private readonly AppendOnlyChunkBuffer<ushort> frontMeasurements = new(MeasurementChunkSize);
    private readonly AppendOnlyChunkBuffer<ushort> rearMeasurements = new(MeasurementChunkSize);
    private readonly AppendOnlyChunkBuffer<ImuRecord> imuRecords = new(ImuChunkSize);
    private readonly AppendOnlyChunkBuffer<GpsRecord> gpsRecords = new(GpsChunkSize);

    private IDisposable? framesSubscription;
    private IDisposable? statesSubscription;
    private ILiveDaqSharedStreamLease? observerLease;
    private ILiveDaqSharedStreamLease? configurationLockLease;
    private Task? displayLoopTask;
    private LiveSessionPresentationSnapshot current = LiveSessionPresentationSnapshot.Empty;
    private LiveSessionHeader? sessionHeader;
    private LiveSessionStats? latestSessionStats;
    private TelemetryData? statisticsTelemetry;
    private SessionDamperPercentages damperPercentages = new(null, null, null, null, null, null, null, null);
    private TrackPoint[] sessionTrackPoints = [];
    private LiveDaqClientDropCounters sharedClientDropCounters = LiveDaqClientDropCounters.Empty;
    private LiveConnectionState connectionState = LiveConnectionState.Disconnected;
    private string? lastError;
    private ulong? captureStartMonotonicUs;
    private DateTimeOffset? captureStartUtc;
    private long captureRevision;
    private long displayEpoch;
    private int queuedDisplayUpdates;
    private long latestStatisticsRevision = -1;
    private long queuedStatisticsRevision = -1;
    private long runningStatisticsRevision = -1;
    private ulong statisticsRecomputesSkipped;
    private ulong graphBatchesCoalesced;
    private ulong graphSamplesDiscarded;
    private Task? statisticsLoopTask;

    private bool hasPublishedSaveableCapture;
    private bool isTerminalClosed;
    private bool isAttached;
    private bool isDisposed;
    private DateTimeOffset nextStatisticsRunAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastClientPressureUtc = DateTimeOffset.MinValue;

    public LiveSessionService(
        LiveDaqSessionContext context,
        ILiveDaqSharedStream sharedStream,
        ISessionPresentationService sessionPresentationService,
        IBackgroundTaskRunner backgroundTaskRunner,
        ILiveGraphPipeline graphPipeline)
    {
        this.context = context;
        this.sharedStream = sharedStream;
        this.sessionPresentationService = sessionPresentationService;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.graphPipeline = graphPipeline;
    }

    public IObservable<LiveSessionPresentationSnapshot> Snapshots => snapshotsSubject.AsObservable();

    public IObservable<LiveGraphBatch> GraphBatches => graphPipeline.GraphBatches;

    public LiveSessionPresentationSnapshot Current => current;

    public async Task EnsureAttachedAsync(CancellationToken cancellationToken = default)
    {
        bool shouldStart;
        bool attachedNow = false;
        IDisposable? attachedFramesSubscription = null;
        IDisposable? attachedStatesSubscription = null;
        ILiveDaqSharedStreamLease? attachedObserverLease = null;
        ILiveDaqSharedStreamLease? attachedConfigurationLockLease = null;

        try
        {
            lock (gate)
            {
                ThrowIfDisposed();
                if (!isAttached)
                {
                    attachedObserverLease = sharedStream.AcquireLease();
                    attachedConfigurationLockLease = sharedStream.AcquireConfigurationLock();
                    graphPipeline.Start();
                    displayLoopTask ??= Task.Run(() => RunDisplayLoopAsync(disposalCts.Token));
                    attachedFramesSubscription = sharedStream.Frames.Subscribe(HandleFrame);
                    attachedStatesSubscription = sharedStream.States.Subscribe(HandleSharedStreamState);
                    observerLease = attachedObserverLease;
                    configurationLockLease = attachedConfigurationLockLease;
                    framesSubscription = attachedFramesSubscription;
                    statesSubscription = attachedStatesSubscription;
                    isAttached = true;
                    attachedNow = true;
                }

                shouldStart = !isTerminalClosed;
            }

            HandleSharedStreamState(sharedStream.CurrentState);

            if (!shouldStart)
            {
                return;
            }

            await sharedStream.EnsureStartedAsync(cancellationToken);
            HandleSharedStreamState(sharedStream.CurrentState);
        }
        catch
        {
            if (attachedNow)
            {
                lock (gate)
                {
                    if (ReferenceEquals(framesSubscription, attachedFramesSubscription))
                    {
                        framesSubscription = null;
                    }

                    if (ReferenceEquals(statesSubscription, attachedStatesSubscription))
                    {
                        statesSubscription = null;
                    }

                    if (ReferenceEquals(configurationLockLease, attachedConfigurationLockLease))
                    {
                        configurationLockLease = null;
                    }

                    if (ReferenceEquals(observerLease, attachedObserverLease))
                    {
                        observerLease = null;
                    }

                    isAttached = false;
                }

                attachedFramesSubscription?.Dispose();
                attachedStatesSubscription?.Dispose();

                if (attachedConfigurationLockLease is not null)
                {
                    await attachedConfigurationLockLease.DisposeAsync();
                }

                if (attachedObserverLease is not null)
                {
                    await attachedObserverLease.DisposeAsync();
                }
            }

            throw;
        }
    }

    public Task ResetCaptureAsync(CancellationToken cancellationToken = default)
    {
        LiveSessionPresentationSnapshot snapshot;
        lock (gate)
        {
            ThrowIfDisposed();
            frontMeasurements.Clear();
            rearMeasurements.Clear();
            imuRecords.Clear();
            gpsRecords.Clear();
            statisticsTelemetry = null;
            damperPercentages = new SessionDamperPercentages(null, null, null, null, null, null, null, null);
            sessionTrackPoints = [];
            captureStartMonotonicUs = null;
            captureStartUtc = null;
            captureRevision++;
            displayEpoch++;
            latestStatisticsRevision = captureRevision;
            queuedStatisticsRevision = -1;
            runningStatisticsRevision = -1;
            statisticsRecomputesSkipped = 0;
            graphBatchesCoalesced = 0;
            graphSamplesDiscarded = 0;
            nextStatisticsRunAt = DateTimeOffset.MinValue;
            lastClientPressureUtc = DateTimeOffset.MinValue;
            hasPublishedSaveableCapture = false;
            snapshot = BuildSnapshotLocked();
        }

        graphPipeline.Reset();
        PublishSnapshot(snapshot);
        return Task.CompletedTask;
    }

    public async Task<LiveSessionCapturePackage> PrepareCaptureForSaveAsync(CancellationToken cancellationToken = default)
    {
        LiveCaptureSnapshot captureSnapshot;

        lock (gate)
        {
            ThrowIfDisposed();
            if (!CanSaveLocked())
            {
                throw new InvalidOperationException("No live capture is available to save.");
            }

            captureSnapshot = CreateCaptureSnapshotLocked();
        }

        var capture = await backgroundTaskRunner.RunAsync(
            () => BuildCapture(captureSnapshot),
            cancellationToken);
        return new LiveSessionCapturePackage(context, capture);
    }

    public async ValueTask DisposeAsync()
    {
        IDisposable? frames;
        IDisposable? states;
        ILiveDaqSharedStreamLease? configurationLock;
        ILiveDaqSharedStreamLease? observer;
        Task? statisticsLoop;
        Task? displayLoop;

        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            frames = framesSubscription;
            states = statesSubscription;
            configurationLock = configurationLockLease;
            observer = observerLease;
            statisticsLoop = statisticsLoopTask;
            displayLoop = displayLoopTask;
            framesSubscription = null;
            statesSubscription = null;
            configurationLockLease = null;
            observerLease = null;
            statisticsLoopTask = null;
            displayLoopTask = null;
        }

        frames?.Dispose();
        states?.Dispose();

        disposalCts.Cancel();
        displayUpdates.Writer.TryComplete();

        if (statisticsLoop is not null)
        {
            try
            {
                await statisticsLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (displayLoop is not null)
        {
            try
            {
                await displayLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (configurationLock is not null)
        {
            await configurationLock.DisposeAsync();
        }

        if (observer is not null)
        {
            await observer.DisposeAsync();
        }

        await graphPipeline.DisposeAsync();

        snapshotsSubject.OnCompleted();
        snapshotsSubject.Dispose();
        disposalCts.Dispose();
    }

    private void HandleSharedStreamState(LiveDaqSharedStreamState state)
    {
        LiveSessionPresentationSnapshot snapshot;
        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

            connectionState = state.ConnectionState;
            lastError = state.LastError;
            if (GetPressureDropTotal(state.ClientDropCounters) > GetPressureDropTotal(sharedClientDropCounters))
            {
                lastClientPressureUtc = DateTimeOffset.UtcNow;
            }

            sharedClientDropCounters = state.ClientDropCounters;

            if (state.SessionHeader is { } nextHeader)
            {
                if (sessionHeader is not null && nextHeader.SessionId != sessionHeader.SessionId && HasAnyCaptureLocked())
                {
                    isTerminalClosed = true;
                    lastError ??= "DAQ started a new live session.";
                }
                else
                {
                    sessionHeader = nextHeader;
                }
            }

            if (state.IsClosed)
            {
                isTerminalClosed = true;
            }

            snapshot = BuildSnapshotLocked();
        }

        PublishSnapshot(snapshot);
    }

    private void HandleFrame(LiveProtocolFrame frame)
    {
        LiveSessionPresentationSnapshot? snapshotToPublish = null;
        LiveDisplayUpdate? displayUpdate = null;
        var shouldQueueStatistics = false;

        lock (gate)
        {
            if (isDisposed || isTerminalClosed)
            {
                return;
            }

            switch (frame)
            {
                case LiveTravelBatchFrame travelBatchFrame:
                    displayUpdate = ApplyTravelBatchLocked(travelBatchFrame);
                    shouldQueueStatistics = CanBuildStatisticsLocked();
                    if (CanSaveLocked() && !hasPublishedSaveableCapture)
                    {
                        hasPublishedSaveableCapture = true;
                        snapshotToPublish = BuildSnapshotLocked();
                    }
                    break;

                case LiveImuBatchFrame imuBatchFrame:
                    displayUpdate = ApplyImuBatchLocked(imuBatchFrame);
                    break;

                case LiveGpsBatchFrame gpsBatchFrame:
                    ApplyGpsBatchLocked(gpsBatchFrame);
                    snapshotToPublish = BuildSnapshotLocked();
                    break;

                case LiveSessionStatsFrame sessionStatsFrame:
                    latestSessionStats = sessionStatsFrame.Payload;
                    snapshotToPublish = BuildSnapshotLocked();
                    break;
            }
        }

        if (snapshotToPublish is not null)
        {
            PublishSnapshot(snapshotToPublish);
        }

        if (displayUpdate is not null && QueueDisplayUpdate(displayUpdate))
        {
            lock (gate)
            {
                snapshotToPublish = BuildSnapshotLocked();
            }

            PublishSnapshot(snapshotToPublish);
        }

        if (shouldQueueStatistics)
        {
            QueueStatisticsRecompute();
        }
    }

    private LiveDisplayUpdate? ApplyTravelBatchLocked(LiveTravelBatchFrame frame)
    {
        if (sessionHeader is null || frame.Records.Count == 0)
        {
            return null;
        }

        var batchCount = frame.Records.Count;
        var travelTimes = new double[batchCount];
        var frontTravel = new double[batchCount];
        var rearTravel = new double[batchCount];

        InitializeCaptureOriginLocked(frame.Batch.FirstMonotonicUs);

        for (var index = 0; index < batchCount; index++)
        {
            var record = frame.Records[index];
            var monotonicUs = frame.Batch.FirstMonotonicUs + (ulong)index * sessionHeader.TravelPeriodUs;
            var timeOffset = ToSampleOffsetSecondsLocked(monotonicUs);
            travelTimes[index] = timeOffset;
            frontMeasurements.Append(record.ForkAngle);
            rearMeasurements.Append(record.ShockAngle);

            var frontValue = ConvertTravel(record.ForkAngle, context.BikeData.FrontMeasurementToTravel, context.BikeData.FrontMaxTravel);
            var rearValue = ConvertTravel(record.ShockAngle, context.BikeData.RearMeasurementToTravel, context.BikeData.RearMaxTravel);
            frontTravel[index] = frontValue;
            rearTravel[index] = rearValue;
        }

        captureRevision++;
        return new LiveDisplayUpdate.Travel(displayEpoch, travelTimes, frontTravel, rearTravel);
    }

    private LiveDisplayUpdate? ApplyImuBatchLocked(LiveImuBatchFrame frame)
    {
        if (sessionHeader is null || frame.Records.Count == 0)
        {
            return null;
        }

        var activeLocations = sessionHeader.GetActiveImuLocations();
        if (activeLocations.Count == 0)
        {
            return null;
        }

        imuRecords.AppendRange(frame.Records);
        captureRevision++;
        InitializeCaptureOriginLocked(frame.Batch.FirstMonotonicUs);

        var tickCount = (int)frame.Batch.SampleCount;
        var recordsPerTick = activeLocations.Count;
        var perLocationTimes = new double[recordsPerTick][];
        var perLocationMagnitudes = new double[recordsPerTick][];
        var perLocationCounts = new int[recordsPerTick];
        for (var locationIndex = 0; locationIndex < recordsPerTick; locationIndex++)
        {
            perLocationTimes[locationIndex] = new double[tickCount];
            perLocationMagnitudes[locationIndex] = new double[tickCount];
        }

        for (var tickIndex = 0; tickIndex < tickCount; tickIndex++)
        {
            var timeOffset = ToSampleOffsetSecondsLocked(
                frame.Batch.FirstMonotonicUs + (ulong)tickIndex * sessionHeader.ImuPeriodUs);

            for (var locationIndex = 0; locationIndex < recordsPerTick; locationIndex++)
            {
                var recordIndex = tickIndex * recordsPerTick + locationIndex;
                if (recordIndex >= frame.Records.Count)
                {
                    break;
                }

                var location = activeLocations[locationIndex];
                var nextIndex = perLocationCounts[locationIndex]++;
                perLocationTimes[locationIndex][nextIndex] = timeOffset;
                perLocationMagnitudes[locationIndex][nextIndex] =
                    ConvertImuMagnitude(frame.Records[recordIndex], location, sessionHeader.ImuCalibrationScales);
            }
        }

        var imuTimes = new Dictionary<LiveImuLocation, IReadOnlyList<double>>(activeLocations.Count);
        var imuMagnitudes = new Dictionary<LiveImuLocation, IReadOnlyList<double>>(activeLocations.Count);
        for (var locationIndex = 0; locationIndex < activeLocations.Count; locationIndex++)
        {
            var location = activeLocations[locationIndex];
            var sampleCount = perLocationCounts[locationIndex];
            imuTimes[location] = TrimSeries(perLocationTimes[locationIndex], sampleCount);
            imuMagnitudes[location] = TrimSeries(perLocationMagnitudes[locationIndex], sampleCount);
        }

        return new LiveDisplayUpdate.Imu(displayEpoch, imuTimes, imuMagnitudes);

        static IReadOnlyList<double> TrimSeries(double[] values, int count)
        {
            if (count == values.Length)
            {
                return values;
            }

            if (count == 0)
            {
                return Array.Empty<double>();
            }

            var trimmed = new double[count];
            Array.Copy(values, trimmed, count);
            return trimmed;
        }
    }

    private void ApplyGpsBatchLocked(LiveGpsBatchFrame frame)
    {
        if (frame.Records.Count == 0)
        {
            return;
        }

        InitializeCaptureOriginLocked(frame.Batch.FirstMonotonicUs);

        var appendedTrackPoints = new List<TrackPoint>(frame.Records.Count);
        var fallbackToFullProjection = false;
        var lastTrackPointTime = sessionTrackPoints.Length == 0
            ? double.NegativeInfinity
            : sessionTrackPoints[^1].Time;

        foreach (var record in frame.Records)
        {
            gpsRecords.Append(record);

            var projected = GpsTrackPointProjection.TryProject(record);
            if (projected is null)
            {
                continue;
            }

            if (projected.Time < lastTrackPointTime)
            {
                fallbackToFullProjection = true;
                break;
            }

            appendedTrackPoints.Add(projected);
            lastTrackPointTime = projected.Time;
        }

        if (fallbackToFullProjection)
        {
            sessionTrackPoints = [.. GpsTrackPointProjection.ProjectAll(gpsRecords.CreateSnapshot().ToArray())];
        }
        else if (appendedTrackPoints.Count > 0)
        {
            var updatedTrackPoints = new TrackPoint[sessionTrackPoints.Length + appendedTrackPoints.Count];
            Array.Copy(sessionTrackPoints, updatedTrackPoints, sessionTrackPoints.Length);
            appendedTrackPoints.CopyTo(updatedTrackPoints, sessionTrackPoints.Length);
            sessionTrackPoints = updatedTrackPoints;
        }

        captureRevision++;
    }

    private bool QueueDisplayUpdate(LiveDisplayUpdate update)
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

        if (droppedOldest)
        {
            lock (gate)
            {
                graphBatchesCoalesced++;
                graphSamplesDiscarded += (ulong)update.SampleCount;
                lastClientPressureUtc = DateTimeOffset.UtcNow;
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

                long currentEpoch;
                lock (gate)
                {
                    if (isDisposed)
                    {
                        return;
                    }

                    currentEpoch = displayEpoch;
                }

                if (update.Epoch != currentEpoch)
                {
                    continue;
                }

                switch (update)
                {
                    case LiveDisplayUpdate.Travel travel:
                        graphPipeline.AppendTravelSamples(travel.Times, travel.FrontTravel, travel.RearTravel);
                        break;

                    case LiveDisplayUpdate.Imu imu:
                        foreach (var entry in imu.Times)
                        {
                            if (!imu.Magnitudes.TryGetValue(entry.Key, out var magnitudes))
                            {
                                continue;
                            }

                            var times = entry.Value as double[] ?? entry.Value.ToArray();
                            var magnitudeValues = magnitudes as double[] ?? magnitudes.ToArray();
                            graphPipeline.AppendImuSamples(entry.Key, times, magnitudeValues);
                        }
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private void QueueStatisticsRecompute()
    {
        lock (gate)
        {
            if (!CanBuildStatisticsLocked() || isDisposed)
            {
                return;
            }

            if (IsStatisticsPressureQuietPeriodActiveLocked(DateTimeOffset.UtcNow))
            {
                statisticsRecomputesSkipped++;
                return;
            }

            var nextRevision = captureRevision;
            var activeRevision = Math.Max(latestStatisticsRevision, runningStatisticsRevision);
            if (queuedStatisticsRevision > activeRevision && nextRevision > queuedStatisticsRevision)
            {
                statisticsRecomputesSkipped += (ulong)(nextRevision - queuedStatisticsRevision);
            }

            queuedStatisticsRevision = nextRevision;
            if (statisticsLoopTask is null || statisticsLoopTask.IsCompleted)
            {
                statisticsLoopTask = Task.Run(() => RunStatisticsLoopAsync(disposalCts.Token));
            }
        }
    }

    private async Task RunStatisticsLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            LiveCaptureSnapshot capture;
            long revision;

            lock (gate)
            {
                if (isDisposed || !CanBuildStatisticsLocked() || queuedStatisticsRevision <= latestStatisticsRevision)
                {
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                delay = nextStatisticsRunAt > now ? nextStatisticsRunAt - now : TimeSpan.Zero;
            }

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }

            lock (gate)
            {
                if (isDisposed || !CanBuildStatisticsLocked() || queuedStatisticsRevision <= latestStatisticsRevision)
                {
                    return;
                }

                revision = queuedStatisticsRevision;
                capture = CreateCaptureSnapshotLocked();
                runningStatisticsRevision = revision;
                nextStatisticsRunAt = DateTimeOffset.UtcNow.AddMilliseconds(SessionGraphSettings.LiveStatisticsRefreshIntervalMs);
            }

            try
            {
                var telemetryData = await backgroundTaskRunner.RunAsync(
                    () => TelemetryData.FromLiveCapture(BuildCapture(capture)),
                    cancellationToken);
                var percentages = sessionPresentationService.CalculateDamperPercentages(telemetryData);

                LiveSessionPresentationSnapshot snapshot;
                bool shouldContinue;
                lock (gate)
                {
                    if (isDisposed)
                    {
                        return;
                    }

                    if (revision >= latestStatisticsRevision)
                    {
                        statisticsTelemetry = telemetryData;
                        damperPercentages = percentages;
                        latestStatisticsRevision = revision;
                    }

                    if (runningStatisticsRevision == revision)
                    {
                        runningStatisticsRevision = -1;
                    }

                    snapshot = BuildSnapshotLocked();
                    shouldContinue = queuedStatisticsRevision > revision;
                }

                PublishSnapshot(snapshot);

                if (!shouldContinue)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Live session statistics recompute failed for {IdentityKey}", context.IdentityKey);

                lock (gate)
                {
                    if (runningStatisticsRevision == revision)
                    {
                        runningStatisticsRevision = -1;
                    }

                    if (queuedStatisticsRevision <= revision)
                    {
                        return;
                    }
                }
            }
        }
    }

    private LiveSessionPresentationSnapshot BuildSnapshotLocked()
    {
        return new LiveSessionPresentationSnapshot(
            Stream: BuildStreamPresentationLocked(),
            StatisticsTelemetry: statisticsTelemetry,
            DamperPercentages: damperPercentages,
            SessionTrackPoints: sessionTrackPoints,
            Controls: BuildControlsLocked(),
            CaptureRevision: captureRevision);
    }

    private LiveSessionStreamPresentation BuildStreamPresentationLocked()
    {
        if (isTerminalClosed)
        {
            return new LiveSessionStreamPresentation.Closed(lastError);
        }

        return connectionState switch
        {
            LiveConnectionState.Connecting => new LiveSessionStreamPresentation.Connecting(),
            LiveConnectionState.Connected when sessionHeader is not null => new LiveSessionStreamPresentation.Streaming(
                sessionHeader.SessionStartUtc.LocalDateTime,
                sessionHeader),
            _ => new LiveSessionStreamPresentation.Idle(),
        };
    }

    private LiveSessionControlState BuildControlsLocked()
    {
        return new LiveSessionControlState(
            ConnectionState: connectionState,
            LastError: lastError,
            SessionHeader: sessionHeader,
            CaptureStartUtc: captureStartUtc,
            CaptureDuration: CalculateCaptureDurationLocked(),
            TravelQueueDepth: latestSessionStats?.TravelQueueDepth ?? 0,
            ImuQueueDepth: latestSessionStats?.ImuQueueDepth ?? 0,
            GpsQueueDepth: latestSessionStats?.GpsQueueDepth ?? 0,
            TravelDroppedBatches: latestSessionStats?.TravelDroppedBatches ?? 0,
            ImuDroppedBatches: latestSessionStats?.ImuDroppedBatches ?? 0,
            GpsDroppedBatches: latestSessionStats?.GpsDroppedBatches ?? 0,
            CanSave: CanSaveLocked())
        {
            ClientDropCounters = sharedClientDropCounters.Add(
                LiveDaqClientDropCounters.Empty with
                {
                    GraphBatchesCoalesced = graphBatchesCoalesced,
                    GraphSamplesDiscarded = graphSamplesDiscarded,
                    StatisticsRecomputesSkipped = statisticsRecomputesSkipped,
                }),
        };
    }

    private bool IsStatisticsPressureQuietPeriodActiveLocked(DateTimeOffset now)
    {
        return lastClientPressureUtc != DateTimeOffset.MinValue
            && now - lastClientPressureUtc < StatisticsPressureQuietPeriod;
    }

    private static ulong GetPressureDropTotal(LiveDaqClientDropCounters counters) =>
        counters.RawTelemetryFramesSkipped
        + counters.ParsedTelemetryFramesDropped
        + counters.SubscriberFramesDropped
        + counters.GraphBatchesCoalesced
        + counters.GraphSamplesDiscarded;

    private LiveCaptureSnapshot CreateCaptureSnapshotLocked()
    {
        return new LiveCaptureSnapshot(
            Metadata: new Metadata
            {
                SourceName = context.DisplayName,
                Version = 4,
                SampleRate = (int)(sessionHeader?.AcceptedTravelHz ?? 0),
                Timestamp = (captureStartUtc ?? sessionHeader?.SessionStartUtc ?? DateTimeOffset.UnixEpoch).ToUnixTimeSeconds(),
                Duration = CalculateCaptureDurationLocked().TotalSeconds,
            },
            SessionHeader: sessionHeader,
            FrontMeasurements: frontMeasurements.CreateSnapshot(),
            RearMeasurements: rearMeasurements.CreateSnapshot(),
            ImuRecords: imuRecords.CreateSnapshot(),
            GpsRecords: gpsRecords.CreateSnapshot());
    }

    private LiveTelemetryCapture BuildCapture(LiveCaptureSnapshot snapshot)
    {
        return new LiveTelemetryCapture(
            Metadata: snapshot.Metadata,
            BikeData: context.BikeData,
            FrontMeasurements: snapshot.FrontMeasurements.ToArray(),
            RearMeasurements: snapshot.RearMeasurements.ToArray(),
            ImuData: BuildImuCapture(snapshot),
            GpsData: snapshot.GpsRecords.Count == 0 ? null : snapshot.GpsRecords.ToArray(),
            Markers: []);
    }

    private static RawImuData? BuildImuCapture(LiveCaptureSnapshot snapshot)
    {
        if (snapshot.SessionHeader is null || snapshot.ImuRecords.Count == 0)
        {
            return null;
        }

        var activeLocations = snapshot.SessionHeader.GetActiveImuLocations();
        return new RawImuData
        {
            SampleRate = (int)snapshot.SessionHeader.AcceptedImuHz,
            ActiveLocations = activeLocations.Select(location => (byte)location).ToList(),
            Meta =
            [
                .. activeLocations.Select(location => new ImuMetaEntry(
                    LocationId: (byte)location,
                    AccelLsbPerG: snapshot.SessionHeader.ImuCalibrationScales.GetAccelScale(location),
                    GyroLsbPerDps: snapshot.SessionHeader.ImuCalibrationScales.GetGyroScale(location)))
            ],
            Records = [.. snapshot.ImuRecords.ToArray()]
        };
    }

    private void PublishSnapshot(LiveSessionPresentationSnapshot snapshot)
    {
        current = snapshot;
        snapshotsSubject.OnNext(snapshot);
    }

    private static double ConvertTravel(ushort measurement, Func<ushort, double>? measurementToTravel, double? maxTravel)
    {
        if (measurementToTravel is null)
        {
            return double.NaN;
        }

        var travel = measurementToTravel(measurement);
        if (!double.IsFinite(travel))
        {
            return double.NaN;
        }

        return maxTravel is double finiteMaxTravel
            ? Math.Clamp(travel, 0, finiteMaxTravel)
            : travel;
    }

    private static double ConvertImuMagnitude(ImuRecord record, LiveImuLocation location, LiveImuCalibrationScales scales)
    {
        var accelScale = scales.GetAccelScale(location);
        if (accelScale == 0)
        {
            return double.NaN;
        }

        var ax = record.Ax / accelScale;
        var ay = record.Ay / accelScale;
        var az = record.Az / accelScale - 1.0;
        return Math.Sqrt(ax * ax + ay * ay + az * az);
    }

    private double ToSampleOffsetSecondsLocked(ulong sampleMonotonicUs)
    {
        if (sessionHeader is null)
        {
            return 0;
        }

        var captureStartUs = captureStartMonotonicUs ?? sessionHeader.SessionStartMonotonicUs;
        var deltaUs = sampleMonotonicUs >= captureStartUs
            ? sampleMonotonicUs - captureStartUs
            : 0;
        return deltaUs / 1_000_000.0;
    }

    private TimeSpan CalculateCaptureDurationLocked()
    {
        if (captureStartUtc is null)
        {
            return TimeSpan.Zero;
        }

        var measurementCount = Math.Max(frontMeasurements.Count, rearMeasurements.Count);
        if (measurementCount > 0 && sessionHeader is not null && sessionHeader.AcceptedTravelHz > 0)
        {
            return TimeSpan.FromSeconds(measurementCount / (double)sessionHeader.AcceptedTravelHz);
        }

        if (sessionTrackPoints.Length > 0)
        {
            var duration = TimeSpan.FromSeconds(
                sessionTrackPoints[^1].Time - captureStartUtc.Value.ToUnixTimeSeconds());
            return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        }

        return TimeSpan.Zero;
    }

    private void InitializeCaptureOriginLocked(ulong sampleMonotonicUs)
    {
        if (sessionHeader is null || captureStartMonotonicUs is not null)
        {
            return;
        }

        captureStartMonotonicUs = sampleMonotonicUs;
        var deltaUs = sampleMonotonicUs >= sessionHeader.SessionStartMonotonicUs
            ? sampleMonotonicUs - sessionHeader.SessionStartMonotonicUs
            : 0;
        captureStartUtc = sessionHeader.SessionStartUtc.AddMilliseconds(deltaUs / 1000.0);
    }

    private bool CanSaveLocked()
    {
        return frontMeasurements.Count >= 5 || rearMeasurements.Count >= 5;
    }

    private bool CanBuildStatisticsLocked()
    {
        return CanSaveLocked() && sessionHeader is not null && sessionHeader.AcceptedTravelHz > 0;
    }

    private bool HasAnyCaptureLocked()
    {
        return frontMeasurements.Count > 0 || rearMeasurements.Count > 0 || imuRecords.Count > 0 || gpsRecords.Count > 0;
    }

    private void ThrowIfDisposed()
    {
        if (isDisposed)
        {
            throw new ObjectDisposedException(nameof(LiveSessionService));
        }
    }
}
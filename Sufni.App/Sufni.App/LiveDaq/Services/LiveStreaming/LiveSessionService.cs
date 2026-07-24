using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Sufni.Telemetry;
using Serilog;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.LiveDaq.Queries;
using Sufni.App.LiveDaq.Services.Imu;
#if SUFNI_PROFILING_DIAGNOSTICS
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services;
using Sufni.Profiling;
#endif
using Sufni.App.Sessions.Services;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Shared.Plots;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

internal sealed class LiveSessionServiceFactory(
    ISessionPresentationService sessionPresentationService,
    IBackgroundTaskRunner backgroundTaskRunner,
    LiveSignalPipelineFactory liveSignalPipelineFactory) : ILiveSessionServiceFactory
{
    public ILiveSessionService Create(LiveDaqSessionContext context, ILiveDaqSharedStream sharedStream)
    {
        return new LiveSessionService(
            context,
            sharedStream,
            sessionPresentationService,
            backgroundTaskRunner,
            liveSignalPipelineFactory.Create());
    }
}

internal sealed class LiveSessionService : ILiveSessionService
{
    private const int GpsChunkSize = 256;
    private const int TemperatureChunkSize = 64;
    private const int DisplayUpdateQueueCapacity = 8;
    private static readonly TimeSpan AnalysisPressureQuietPeriod = TimeSpan.FromMilliseconds(500);
#if SUFNI_PROFILING_DIAGNOSTICS
    private static readonly int[] ProfilingCheckpointSeconds = [15, 30, 60, 90, 120];

    private sealed record ProfilingCaptureCounts(
        long Front,
        long Rear,
        IReadOnlyDictionary<LiveImuLocation, long> Imu,
        long Gps,
        long Temperature,
        long Markers,
        long Gaps)
    {
        public long Total => Front + Rear + Imu.Values.Sum() + Gps + Temperature + Markers + Gaps;
    }
#endif

    private static readonly ILogger logger = Log.ForContext<LiveSessionService>();

    private readonly record struct LiveCaptureSnapshot(
        Metadata Metadata,
        LiveSessionHeader? SessionHeader,
        FixedRateSegment<ushort>[] FrontTravelSegments,
        FixedRateSegment<ushort>[] RearTravelSegments,
        IReadOnlyDictionary<LiveImuLocation, FixedRateSegment<ImuRecord>[]> ImuSegmentsByLocation,
        ChunkedBufferSnapshot<GpsRecord> GpsRecords,
        ChunkedBufferSnapshot<TemperatureSample> TemperatureSamples,
        MarkerData[] Markers,
        RawStreamGap[] StreamGaps,
        SstFinalStatus? FinalStatus,
        bool MissingFinalStatus);

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
            IReadOnlyDictionary<LiveImuLocation, IReadOnlyList<double>> VibrationRms,
            FramePitchRollSeries? FramePitchRoll) : LiveDisplayUpdate(Epoch)
        {
            public override int SampleCount => Times.Values.Sum(series => series.Count) +
                (FramePitchRoll?.Times.Length ?? 0);
        }
    }

    private readonly LiveDaqSessionContext context;
    private readonly ILiveDaqSharedStream sharedStream;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly ILiveSignalPipeline signalPipeline;
    private readonly LiveImuDisplaySignalProcessor imuDisplaySignalProcessor = new();
    private readonly System.Threading.Lock gate = new();
    private readonly System.Threading.Lock displayQueueGate = new();
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

    private readonly AppendOnlyChunkBuffer<GpsRecord> gpsRecords = new(GpsChunkSize);
    private readonly AppendOnlyChunkBuffer<TemperatureSample> temperatureSamples = new(TemperatureChunkSize);
    private readonly List<MarkerData> markers = [];
    private readonly List<RawStreamGap> streamGaps = [];
    private readonly FixedRateSegmentBuilder<ushort> frontTravelBuilder;
    private readonly FixedRateSegmentBuilder<ushort> rearTravelBuilder;
    private readonly Dictionary<LiveImuLocation, FixedRateSegmentBuilder<ImuRecord>> imuBuilders = [];

    private IDisposable? framesSubscription;
    private IDisposable? statesSubscription;
    private ILiveDaqSharedStreamLease? observerLease;
    private ILiveDaqSharedStreamLease? configurationLockLease;
    private Task? displayLoopTask;
    private LiveSessionPresentationSnapshot current = LiveSessionPresentationSnapshot.Empty;
    private LiveSessionHeader? sessionHeader;
    private LiveSessionStats? latestSessionStats;
    private TelemetryData? analysisTelemetry;
    private SessionDampingPercentages dampingPercentages = SessionDampingPercentages.Empty;
    private TrackPoint[] sessionTrackPoints = [];
    private TrackPointGeoCoordinate? previousAcceptedGpsCoordinate;
    private LiveDaqClientDropCounters sharedClientDropCounters = LiveDaqClientDropCounters.Empty;
    private SstFinalStatus? finalStatus;
    private LiveConnectionState connectionState = LiveConnectionState.Disconnected;
    private string? lastError;
    private ulong? captureStartMonotonicUs;
    private DateTimeOffset? captureStartUtc;
#if SUFNI_PROFILING_DIAGNOSTICS
    private string? profilingCaptureCorrelationId;
    private ProfilingResourceSampler? profilingResourceSampler;
    private int profilingNextCheckpointIndex;
    private ulong? profilingFirstTravelIndex;
    private ulong? profilingLastTravelIndex;
#endif
    private long captureRevision;
    private long displayEpoch;
    private int queuedDisplayUpdates;
    private long latestAnalysisRevision = -1;
    private long queuedAnalysisRevision = -1;
    private long runningAnalysisRevision = -1;
    private ulong analysisRecomputesSkipped;
    private ulong signalBatchesCoalesced;
    private ulong signalSamplesDiscarded;
    private Task? analysisLoopTask;

    private bool hasPublishedSaveableCapture;
    private bool isTerminalClosed;
    private bool isAttached;
    private bool isDisposed;
    private DateTimeOffset nextAnalysisRunAt = DateTimeOffset.MinValue;
    private DateTimeOffset lastClientPressureUtc = DateTimeOffset.MinValue;

    public LiveSessionService(
        LiveDaqSessionContext context,
        ILiveDaqSharedStream sharedStream,
        ISessionPresentationService sessionPresentationService,
        IBackgroundTaskRunner backgroundTaskRunner,
        ILiveSignalPipeline signalPipeline)
    {
        this.context = context;
        this.sharedStream = sharedStream;
        this.sessionPresentationService = sessionPresentationService;
        this.backgroundTaskRunner = backgroundTaskRunner;
        this.signalPipeline = signalPipeline;
        frontTravelBuilder = new FixedRateSegmentBuilder<ushort>(
            SstV5ProtocolConstants.StreamTravel,
            (byte)SstV5ProtocolConstants.SensorForkTravel,
            AddStreamGap);
        rearTravelBuilder = new FixedRateSegmentBuilder<ushort>(
            SstV5ProtocolConstants.StreamTravel,
            (byte)SstV5ProtocolConstants.SensorShockTravel,
            AddStreamGap);
    }

    public IObservable<LiveSessionPresentationSnapshot> Snapshots => snapshotsSubject.AsObservable();

    public IObservable<LiveSignalBatch> SignalBatches => signalPipeline.SignalBatches;

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
                    signalPipeline.Start();
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
#if SUFNI_PROFILING_DIAGNOSTICS
            CompleteProfilingCaptureLocked("reset");
#endif
            frontTravelBuilder.Clear();
            rearTravelBuilder.Clear();
            foreach (var builder in imuBuilders.Values)
            {
                builder.Clear();
            }

            gpsRecords.Clear();
            temperatureSamples.Clear();
            markers.Clear();
            streamGaps.Clear();
            finalStatus = null;
            imuDisplaySignalProcessor.Reset();
            analysisTelemetry = null;
            dampingPercentages = SessionDampingPercentages.Empty;
            sessionTrackPoints = [];
            previousAcceptedGpsCoordinate = null;
            captureStartMonotonicUs = null;
            captureStartUtc = null;
            captureRevision++;
            displayEpoch++;
            latestAnalysisRevision = captureRevision;
            queuedAnalysisRevision = -1;
            runningAnalysisRevision = -1;
            analysisRecomputesSkipped = 0;
            signalBatchesCoalesced = 0;
            signalSamplesDiscarded = 0;
            nextAnalysisRunAt = DateTimeOffset.MinValue;
            lastClientPressureUtc = DateTimeOffset.MinValue;
            hasPublishedSaveableCapture = false;
            snapshot = BuildSnapshotLocked();
        }

        signalPipeline.Reset();
        PublishSnapshot(snapshot);
        return Task.CompletedTask;
    }

    public async Task<LiveSessionCapturePackage> PrepareCaptureForSaveAsync(CancellationToken cancellationToken = default)
    {
        LiveCaptureSnapshot captureSnapshot;
#if SUFNI_PROFILING_DIAGNOSTICS
        string? correlationId;
#endif

        lock (gate)
        {
            ThrowIfDisposed();
            if (!CanSaveLocked())
            {
                throw new InvalidOperationException("No live capture is available to save.");
            }

            captureSnapshot = CreateCaptureSnapshotLocked();
#if SUFNI_PROFILING_DIAGNOSTICS
            correlationId = profilingCaptureCorrelationId;
            EmitProfilingCheckpointLocked("save", CalculateCaptureDurationLocked().TotalSeconds);
#endif
        }

#if SUFNI_PROFILING_DIAGNOSTICS
        using var profilingStage = ProfilingRuntime.BeginStage(
            ProfilingBench01.Scenario,
            "LiveCapture.BuildCapture",
            correlationId);
#endif
        var capture = await backgroundTaskRunner.RunAsync(
            () => BuildCapture(captureSnapshot),
            cancellationToken);
#if SUFNI_PROFILING_DIAGNOSTICS
        var itemCount = CountCaptureItems(capture);
        profilingStage?.SetResult(itemCount, 0);
        EmitCaptureMeasurements(capture, correlationId);
#endif
        return new LiveSessionCapturePackage(context, capture);
    }

    public async ValueTask DisposeAsync()
    {
        IDisposable? frames;
        IDisposable? states;
        ILiveDaqSharedStreamLease? configurationLock;
        ILiveDaqSharedStreamLease? observer;
        Task? analysisLoop;
        Task? displayLoop;

        lock (gate)
        {
            if (isDisposed)
            {
                return;
            }

#if SUFNI_PROFILING_DIAGNOSTICS
            CompleteProfilingCaptureLocked("dispose");
#endif
            isDisposed = true;
            frames = framesSubscription;
            states = statesSubscription;
            configurationLock = configurationLockLease;
            observer = observerLease;
            analysisLoop = analysisLoopTask;
            displayLoop = displayLoopTask;
            framesSubscription = null;
            statesSubscription = null;
            configurationLockLease = null;
            observerLease = null;
            analysisLoopTask = null;
            displayLoopTask = null;
        }

        frames?.Dispose();
        states?.Dispose();

        disposalCts.Cancel();
        displayUpdates.Writer.TryComplete();

        if (analysisLoop is not null)
        {
            try
            {
                await analysisLoop;
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

        await signalPipeline.DisposeAsync();

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
                    if (sessionHeader is null || nextHeader.SessionId != sessionHeader.SessionId)
                    {
                        imuDisplaySignalProcessor.Reset();
                        imuBuilders.Clear();
                    }

                    sessionHeader = nextHeader;
                    EnsureImuBuildersLocked();
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
        var shouldQueueAnalysis = false;

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
                    shouldQueueAnalysis = CanBuildAnalysisLocked();
                    if (CanSaveLocked() && !hasPublishedSaveableCapture)
                    {
                        hasPublishedSaveableCapture = true;
                        snapshotToPublish = BuildSnapshotLocked();
                    }
                    break;

                case LiveImuBatchFrame imuBatchFrame:
                    displayUpdate = ApplyImuBatchLocked(imuBatchFrame);
                    break;

                case LiveTemperatureBatchFrame temperatureBatchFrame:
                    ApplyTemperatureBatchLocked(temperatureBatchFrame);
                    shouldQueueAnalysis = CanBuildAnalysisLocked();
                    break;

                case LiveGpsBatchFrame gpsBatchFrame:
                    ApplyGpsBatchLocked(gpsBatchFrame);
                    snapshotToPublish = BuildSnapshotLocked();
                    break;

                case LiveSessionStatsFrame sessionStatsFrame:
                    latestSessionStats = sessionStatsFrame.Payload;
                    snapshotToPublish = BuildSnapshotLocked();
                    break;

                case LiveStatusFrame statusFrame:
                    latestSessionStats = LiveProtocolHelpers.CreateSessionStatsFromStatus(statusFrame.Streams);
                    snapshotToPublish = BuildSnapshotLocked();
                    break;

                case LiveMarkerBatchFrame markerBatchFrame:
                    ApplyMarkerBatchLocked(markerBatchFrame);
                    snapshotToPublish = BuildSnapshotLocked();
                    break;

                case LiveSessionResultFrame sessionResultFrame:
                    finalStatus = sessionResultFrame.Payload.FinalStatus;
#if SUFNI_PROFILING_DIAGNOSTICS
                    if (profilingCaptureCorrelationId is not null)
                    {
                        ProfilingRuntime.Marker(
                            ProfilingBench01.Scenario,
                            "CaptureTerminal",
                            profilingCaptureCorrelationId,
                            DescribeFinalStatus(finalStatus));
                    }
#endif
                    snapshotToPublish = BuildSnapshotLocked();
                    break;

                case LiveBatteryBatchFrame:
                    break;
            }

#if SUFNI_PROFILING_DIAGNOSTICS
            if (frame is LiveGpsBatchFrame)
            {
                TryEmitProfilingTimeCheckpointsLocked();
            }
#endif
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

        if (shouldQueueAnalysis)
        {
            QueueAnalysisRecompute();
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
        var firstDeltaUs = GetFirstMonotonicDeltaUs(frame.Batch);
        var effectiveValidityMask = frame.Batch.ValidityMask & sessionHeader.AcceptedSensorMask;
        var frontAccepted = sessionHeader.AcceptedSensorMask.HasFlag(LiveSensorInstanceMask.ForkTravel);
        var rearAccepted = sessionHeader.AcceptedSensorMask.HasFlag(LiveSensorInstanceMask.ShockTravel);
        var frontValid = frontAccepted && effectiveValidityMask.HasFlag(LiveSensorInstanceMask.ForkTravel);
        var rearValid = rearAccepted && effectiveValidityMask.HasFlag(LiveSensorInstanceMask.ShockTravel);

        if (frontAccepted && !frontValid)
        {
            frontTravelBuilder.AddInvalidRange(
                frame.Batch.FirstIndex,
                frame.Batch.SampleCount,
                firstDeltaUs,
                sessionHeader.AcceptedTravelRateMhz);
        }

        if (rearAccepted && !rearValid)
        {
            rearTravelBuilder.AddInvalidRange(
                frame.Batch.FirstIndex,
                frame.Batch.SampleCount,
                firstDeltaUs,
                sessionHeader.AcceptedTravelRateMhz);
        }

        InitializeCaptureOriginLocked(frame.Batch.FirstMonotonicUs);

        for (var index = 0; index < batchCount; index++)
        {
            var record = frame.Records[index];
            var monotonicUs = frame.Batch.FirstMonotonicUs + (ulong)index * sessionHeader.TravelPeriodUs;
            var monotonicDeltaUs = firstDeltaUs + SstV5CompactPayloadDecoder.RoundDurationUs((ulong)index, sessionHeader.AcceptedTravelRateMhz);
            var sampleIndex = frame.Batch.FirstIndex + (ulong)index;
            var timeOffset = ToSampleOffsetSecondsLocked(monotonicUs);
            travelTimes[index] = timeOffset;

            if (frontValid)
            {
                frontTravelBuilder.AddValidSample(sampleIndex, monotonicDeltaUs, record.ForkAngle, sessionHeader.AcceptedTravelRateMhz);
                frontTravel[index] = ConvertTravel(record.ForkAngle, context.BikeData.FrontMeasurementToTravel, context.BikeData.FrontMaxTravel);
            }
            else
            {
                frontTravel[index] = double.NaN;
            }

            if (rearValid)
            {
                rearTravelBuilder.AddValidSample(sampleIndex, monotonicDeltaUs, record.ShockAngle, sessionHeader.AcceptedTravelRateMhz);
                rearTravel[index] = ConvertTravel(record.ShockAngle, context.BikeData.RearMeasurementToTravel, context.BikeData.RearMaxTravel);
            }
            else
            {
                rearTravel[index] = double.NaN;
            }
        }

#if SUFNI_PROFILING_DIAGNOSTICS
        if (profilingCaptureCorrelationId is not null && (frontValid || rearValid))
        {
            profilingFirstTravelIndex ??= frame.Batch.FirstIndex;
            profilingLastTravelIndex = frame.Batch.FirstIndex + frame.Batch.SampleCount - 1;
        }
#endif
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

        EnsureImuBuildersLocked();
        var firstDeltaUs = GetFirstMonotonicDeltaUs(frame.Batch);
        var effectiveValidityMask = frame.Batch.ValidityMask & sessionHeader.AcceptedSensorMask;
        var validLocations = new List<LiveImuLocation>(activeLocations.Count);
        foreach (var location in activeLocations)
        {
            var sourceMask = GetImuSourceMask(location);
            if ((effectiveValidityMask & sourceMask) == 0)
            {
                if (imuBuilders.TryGetValue(location, out var builder))
                {
                    builder.AddInvalidRange(
                        frame.Batch.FirstIndex,
                        frame.Batch.SampleCount,
                        firstDeltaUs,
                        sessionHeader.AcceptedImuRateMhz);
                }
            }
            else
            {
                validLocations.Add(location);
            }
        }

        captureRevision++;
        InitializeCaptureOriginLocked(frame.Batch.FirstMonotonicUs);

        var tickCount = (int)frame.Batch.SampleCount;
        var recordsPerTick = activeLocations.Count;
        var perLocationTimes = new double[recordsPerTick][];
        var perLocationRecords = new ImuRecord[recordsPerTick][];
        var perLocationCounts = new int[recordsPerTick];
        for (var locationIndex = 0; locationIndex < recordsPerTick; locationIndex++)
        {
            perLocationTimes[locationIndex] = new double[tickCount];
            perLocationRecords[locationIndex] = new ImuRecord[tickCount];
        }

        for (var tickIndex = 0; tickIndex < tickCount; tickIndex++)
        {
            var timeOffset = ToSampleOffsetSecondsLocked(
                frame.Batch.FirstMonotonicUs + (ulong)tickIndex * sessionHeader.ImuPeriodUs);

            for (var locationIndex = 0; locationIndex < recordsPerTick; locationIndex++)
            {
                var location = activeLocations[locationIndex];
                var recordIndex = tickIndex * recordsPerTick + locationIndex;
                var sourceMask = GetImuSourceMask(location);
                if ((effectiveValidityMask & sourceMask) == 0)
                {
                    continue;
                }

                var validLocationIndex = validLocations.IndexOf(location);
                recordIndex = tickIndex * validLocations.Count + validLocationIndex;
                if (recordIndex >= frame.Records.Count)
                {
                    break;
                }

                var nextIndex = perLocationCounts[locationIndex]++;
                var record = frame.Records[recordIndex];
                perLocationTimes[locationIndex][nextIndex] = timeOffset;
                perLocationRecords[locationIndex][nextIndex] = record;
                imuBuilders[location].AddValidSample(
                    frame.Batch.FirstIndex + (ulong)tickIndex,
                    firstDeltaUs + SstV5CompactPayloadDecoder.RoundDurationUs((ulong)tickIndex, sessionHeader.AcceptedImuRateMhz),
                    record,
                    sessionHeader.AcceptedImuRateMhz);
            }
        }

        var inputSeries = new List<LiveImuDisplayInputSeries>(activeLocations.Count);
        for (var locationIndex = 0; locationIndex < activeLocations.Count; locationIndex++)
        {
            var location = activeLocations[locationIndex];
            var sampleCount = perLocationCounts[locationIndex];
            inputSeries.Add(new LiveImuDisplayInputSeries(
                location,
                TrimSeries(perLocationTimes[locationIndex], sampleCount),
                TrimSeries(perLocationRecords[locationIndex], sampleCount),
                sessionHeader.ImuCalibrationScales.GetAccelScale(location),
                sessionHeader.ImuCalibrationScales.GetGyroScale(location)));
        }

        var displaySeries = imuDisplaySignalProcessor.ProcessBatch(inputSeries, sessionHeader.AcceptedImuHz);
        if (displaySeries.VibrationTimes.Count == 0 && displaySeries.FramePitchRoll is null)
        {
            return null;
        }

        return new LiveDisplayUpdate.Imu(
            displayEpoch,
            displaySeries.VibrationTimes,
            displaySeries.VibrationRms,
            displaySeries.FramePitchRoll);

        static IReadOnlyList<T> TrimSeries<T>(T[] values, int count)
        {
            if (count == values.Length)
            {
                return values;
            }

            if (count == 0)
            {
                return Array.Empty<T>();
            }

            var trimmed = new T[count];
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
        TrackPoint? previousTrackPoint = sessionTrackPoints.Length == 0 ? null : sessionTrackPoints[^1];
        var previousCoordinate = previousAcceptedGpsCoordinate;

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

            var coordinate = new TrackPointGeoCoordinate(record.Latitude, record.Longitude);
            if (previousTrackPoint is not null && previousCoordinate is { } acceptedCoordinate)
            {
                projected.Speed = TrackPointSeries.CalculateSpeed(
                    previousTrackPoint,
                    projected,
                    acceptedCoordinate,
                    coordinate);
            }

            appendedTrackPoints.Add(projected);
            if (sessionTrackPoints.Length + appendedTrackPoints.Count == 2
                && projected.Speed is { } secondSpeed
                && double.IsFinite(secondSpeed))
            {
                var firstPoint = sessionTrackPoints.Length == 0 ? appendedTrackPoints[0] : sessionTrackPoints[0];
                firstPoint.Speed = secondSpeed;
            }

            lastTrackPointTime = projected.Time;
            previousTrackPoint = projected;
            previousCoordinate = coordinate;
        }

        if (fallbackToFullProjection)
        {
            var records = gpsRecords.CreateSnapshot().ToArray();
            sessionTrackPoints = [.. GpsTrackPointProjection.ProjectAll(records)];
            previousAcceptedGpsCoordinate = GetLastProjectedCoordinate(records);
        }
        else if (appendedTrackPoints.Count > 0)
        {
            var updatedTrackPoints = new TrackPoint[sessionTrackPoints.Length + appendedTrackPoints.Count];
            Array.Copy(sessionTrackPoints, updatedTrackPoints, sessionTrackPoints.Length);
            appendedTrackPoints.CopyTo(updatedTrackPoints, sessionTrackPoints.Length);
            sessionTrackPoints = updatedTrackPoints;
            previousAcceptedGpsCoordinate = previousCoordinate;
        }

        captureRevision++;
    }

    private void ApplyTemperatureBatchLocked(LiveTemperatureBatchFrame frame)
    {
        if (frame.Records.Count == 0)
        {
            return;
        }

        InitializeCaptureOriginLocked(frame.Batch.FirstMonotonicUs);
        foreach (var record in frame.Records)
        {
            temperatureSamples.Append(record.Sample);
        }
        captureRevision++;
    }

    private void ApplyMarkerBatchLocked(LiveMarkerBatchFrame frame)
    {
        foreach (var record in frame.Records)
        {
            if (record.MarkerType == SstV5ProtocolConstants.MarkerManualUserMark)
            {
                markers.Add(new MarkerData(record.MonotonicDeltaUs / 1_000_000.0));
            }
        }

        if (frame.Records.Count > 0)
        {
            captureRevision++;
        }
    }

    private void EnsureImuBuildersLocked()
    {
        if (sessionHeader is null)
        {
            return;
        }

        foreach (var location in sessionHeader.GetActiveImuLocations())
        {
            if (imuBuilders.ContainsKey(location))
            {
                continue;
            }

            imuBuilders[location] = new FixedRateSegmentBuilder<ImuRecord>(
                SstV5ProtocolConstants.StreamImu,
                (byte)location,
                AddStreamGap);
        }
    }

    private void AddStreamGap(RawStreamGap gap)
    {
        streamGaps.Add(gap);
    }

    private ulong GetFirstMonotonicDeltaUs(LiveBatchHeader batch)
    {
        if (sessionHeader is null)
        {
            return batch.FirstMonotonicDeltaUs;
        }

        return batch.FirstMonotonicUs >= sessionHeader.SessionStartMonotonicUs
            ? batch.FirstMonotonicUs - sessionHeader.SessionStartMonotonicUs
            : batch.FirstMonotonicDeltaUs;
    }

    private static LiveSensorInstanceMask GetImuSourceMask(LiveImuLocation location) => location switch
    {
        LiveImuLocation.Frame => LiveSensorInstanceMask.FrameImu,
        LiveImuLocation.Fork => LiveSensorInstanceMask.ForkImu,
        LiveImuLocation.Rear => LiveSensorInstanceMask.RearImu,
        _ => LiveSensorInstanceMask.None,
    };

    private static TrackPointGeoCoordinate? GetLastProjectedCoordinate(IEnumerable<GpsRecord> records)
    {
        GpsRecord? lastProjectedRecord = null;
        foreach (var record in records.OrderBy(record => record.Timestamp))
        {
            if (GpsTrackPointProjection.TryProject(record) is not null)
            {
                lastProjectedRecord = record;
            }
        }

        return lastProjectedRecord is null
            ? null
            : new TrackPointGeoCoordinate(lastProjectedRecord.Latitude, lastProjectedRecord.Longitude);
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
                signalBatchesCoalesced++;
                signalSamplesDiscarded += (ulong)update.SampleCount;
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
                        signalPipeline.AppendTravelSamples(travel.Times, travel.FrontTravel, travel.RearTravel);
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
                            signalPipeline.AppendImuSamples(entry.Key, times, vibrationValues);
                        }

                        if (imu.FramePitchRoll is { } pitchRoll)
                        {
                            signalPipeline.AppendFramePitchRollSamples(
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

    private void QueueAnalysisRecompute()
    {
        lock (gate)
        {
            if (!CanBuildAnalysisLocked() || isDisposed)
            {
                return;
            }

            if (IsAnalysisPressureQuietPeriodActiveLocked(DateTimeOffset.UtcNow))
            {
                analysisRecomputesSkipped++;
                return;
            }

            var nextRevision = captureRevision;
            var activeRevision = Math.Max(latestAnalysisRevision, runningAnalysisRevision);
            if (queuedAnalysisRevision > activeRevision && nextRevision > queuedAnalysisRevision)
            {
                analysisRecomputesSkipped += (ulong)(nextRevision - queuedAnalysisRevision);
            }

            queuedAnalysisRevision = nextRevision;
            if (analysisLoopTask is null || analysisLoopTask.IsCompleted)
            {
                analysisLoopTask = Task.Run(() => RunAnalysisLoopAsync(disposalCts.Token));
            }
        }
    }

    private async Task RunAnalysisLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            TimeSpan delay;
            LiveCaptureSnapshot capture;
            long revision;

            lock (gate)
            {
                if (isDisposed || !CanBuildAnalysisLocked() || queuedAnalysisRevision <= latestAnalysisRevision)
                {
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                delay = nextAnalysisRunAt > now ? nextAnalysisRunAt - now : TimeSpan.Zero;
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
                if (isDisposed || !CanBuildAnalysisLocked() || queuedAnalysisRevision <= latestAnalysisRevision)
                {
                    return;
                }

                revision = queuedAnalysisRevision;
                capture = CreateCaptureSnapshotLocked();
                runningAnalysisRevision = revision;
                nextAnalysisRunAt = DateTimeOffset.UtcNow.AddMilliseconds(PlotSettings.LiveAnalysisRefreshIntervalMs);
            }

            try
            {
                var telemetryData = await backgroundTaskRunner.RunAsync(
                    () => TelemetryData.FromLiveCapture(BuildCapture(capture)),
                    cancellationToken);
                var percentages = sessionPresentationService.CalculateDampingPercentages(
                    telemetryData,
                    dampingSpeedCutoffs: context.DampingSpeedCutoffs);

                LiveSessionPresentationSnapshot snapshot;
                bool shouldContinue;
                lock (gate)
                {
                    if (isDisposed)
                    {
                        return;
                    }

                    if (revision >= latestAnalysisRevision)
                    {
                        analysisTelemetry = telemetryData;
                        dampingPercentages = percentages;
                        latestAnalysisRevision = revision;
                    }

                    if (runningAnalysisRevision == revision)
                    {
                        runningAnalysisRevision = -1;
                    }

                    snapshot = BuildSnapshotLocked();
                    shouldContinue = queuedAnalysisRevision > revision;
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
                logger.Warning(ex, "Live session analysis recompute failed for {IdentityKey}", context.IdentityKey);

                lock (gate)
                {
                    if (runningAnalysisRevision == revision)
                    {
                        runningAnalysisRevision = -1;
                    }

                    if (queuedAnalysisRevision <= revision)
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
            AnalysisTelemetry: analysisTelemetry,
            DampingPercentages: dampingPercentages,
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
                    SignalBatchesCoalesced = signalBatchesCoalesced,
                    SignalSamplesDiscarded = signalSamplesDiscarded,
                    AnalysisRecomputesSkipped = analysisRecomputesSkipped,
                }),
        };
    }

    private bool IsAnalysisPressureQuietPeriodActiveLocked(DateTimeOffset now)
    {
        return lastClientPressureUtc != DateTimeOffset.MinValue
            && now - lastClientPressureUtc < AnalysisPressureQuietPeriod;
    }

    private static ulong GetPressureDropTotal(LiveDaqClientDropCounters counters) =>
        counters.RawTelemetryFramesSkipped
        + counters.ParsedTelemetryFramesDropped
        + counters.SubscriberFramesDropped
        + counters.SignalBatchesCoalesced
        + counters.SignalSamplesDiscarded;

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
            FrontTravelSegments: frontTravelBuilder.CreateSnapshot(),
            RearTravelSegments: rearTravelBuilder.CreateSnapshot(),
            ImuSegmentsByLocation: imuBuilders.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.CreateSnapshot()),
            GpsRecords: gpsRecords.CreateSnapshot(),
            TemperatureSamples: temperatureSamples.CreateSnapshot(),
            Markers: [.. markers],
            StreamGaps: [.. streamGaps],
            FinalStatus: finalStatus,
            MissingFinalStatus: sessionHeader?.ProtocolVersion == LiveProtocolVersion.V3 &&
                finalStatus is null &&
                isTerminalClosed);
    }

    private LiveTelemetryCapture BuildCapture(LiveCaptureSnapshot snapshot)
    {
        return new LiveTelemetryCapture(
            Metadata: snapshot.Metadata,
            BikeData: context.BikeData,
            FrontSegments: ToRawCountSegments(snapshot.FrontTravelSegments),
            RearSegments: ToRawCountSegments(snapshot.RearTravelSegments),
            ImuData: BuildImuCapture(snapshot),
            GpsData: snapshot.GpsRecords.Count == 0 ? null : snapshot.GpsRecords.ToArray(),
            Markers: snapshot.Markers,
            StreamGaps: snapshot.StreamGaps,
            FinalStatus: snapshot.FinalStatus,
            MissingFinalStatus: snapshot.MissingFinalStatus)
        {
            TemperatureData = snapshot.TemperatureSamples.ToArray(),
        };
    }

#if SUFNI_PROFILING_DIAGNOSTICS
    private static void EmitCaptureMeasurements(LiveTelemetryCapture capture, string? correlationId)
    {
        if (!ProfilingBench01.IsActive || correlationId is null)
        {
            return;
        }

        EmitCountSegmentMeasurements("FrontTravel", capture.FrontSegments, correlationId);
        EmitCountSegmentMeasurements("RearTravel", capture.RearSegments, correlationId);

        var imuSegments = capture.ImuData?.Segments ?? [];
        foreach (var location in imuSegments.Select(segment => segment.LocationId).Distinct().Order())
        {
            var segments = imuSegments.Where(segment => segment.LocationId == location).ToArray();
            var sampleCount = segments.Sum(segment => (long)segment.Records.LongLength);
            ProfilingRuntime.Checkpoint(
                ProfilingBench01.Scenario,
                $"Capture.Imu.{location}.Samples",
                correlationId,
                sampleCount);
            ProfilingRuntime.Marker(
                ProfilingBench01.Scenario,
                $"Capture.Imu.{location}.Range",
                correlationId,
                DescribeImuSegmentRange(segments));
        }

        ProfilingRuntime.Checkpoint(
            ProfilingBench01.Scenario,
            "Capture.Gps.Samples",
            correlationId,
            capture.GpsData?.LongLength ?? 0);
        ProfilingRuntime.Checkpoint(
            ProfilingBench01.Scenario,
            "Capture.Temperature.Samples",
            correlationId,
            capture.TemperatureData.LongLength);
        ProfilingRuntime.Checkpoint(
            ProfilingBench01.Scenario,
            "Capture.Markers",
            correlationId,
            capture.Markers.LongLength);
        ProfilingRuntime.Checkpoint(
            ProfilingBench01.Scenario,
            "Capture.StreamGaps",
            correlationId,
            capture.StreamGaps.LongLength);
        ProfilingRuntime.Marker(
            ProfilingBench01.Scenario,
            "Capture.FinalStatus",
            correlationId,
            DescribeFinalStatus(capture.FinalStatus));
        ProfilingRuntime.Flush();
    }

    private static void EmitCountSegmentMeasurements(
        string stream,
        RawCountSegment[] segments,
        string correlationId)
    {
        var sampleCount = segments.Sum(segment => (long)segment.Counts.LongLength);
        ProfilingRuntime.Checkpoint(
            ProfilingBench01.Scenario,
            $"Capture.{stream}.Samples",
            correlationId,
            sampleCount,
            sampleCount * sizeof(ushort));
        ProfilingRuntime.Marker(
            ProfilingBench01.Scenario,
            $"Capture.{stream}.Range",
            correlationId,
            DescribeCountSegmentRange(segments));
    }

    private static string DescribeCountSegmentRange(RawCountSegment[] segments)
    {
        var nonEmpty = segments.Where(segment => segment.Counts.Length > 0).ToArray();
        return nonEmpty.Length == 0
            ? $"segments={segments.Length};first=none;last=none"
            : $"segments={segments.Length};first={nonEmpty.Min(segment => segment.FirstIndex)};last={nonEmpty.Max(segment => segment.FirstIndex + (ulong)segment.Counts.LongLength - 1)}";
    }

    private static string DescribeImuSegmentRange(RawImuSegment[] segments)
    {
        var nonEmpty = segments.Where(segment => segment.Records.Length > 0).ToArray();
        return nonEmpty.Length == 0
            ? $"segments={segments.Length};first=none;last=none"
            : $"segments={segments.Length};first={nonEmpty.Min(segment => segment.FirstIndex)};last={nonEmpty.Max(segment => segment.FirstIndex + (ulong)segment.Records.LongLength - 1)}";
    }

    private static long CountCaptureItems(LiveTelemetryCapture capture)
    {
        var count = capture.FrontSegments.Sum(segment => (long)segment.Counts.LongLength) +
            capture.RearSegments.Sum(segment => (long)segment.Counts.LongLength) +
            (capture.GpsData?.LongLength ?? 0) +
            capture.TemperatureData.LongLength +
            capture.Markers.LongLength +
            capture.StreamGaps.LongLength;
        if (capture.ImuData is not null)
        {
            count += capture.ImuData.Segments.Sum(segment => (long)segment.Records.LongLength);
        }

        return count;
    }

    private static string DescribeFinalStatus(SstFinalStatus? status)
    {
        if (status is null)
        {
            return "present=false";
        }

        var streams = string.Join(
            '|',
            status.Streams.Select(stream =>
                $"kind={stream.StreamKind},producerMissed={stream.ProducerMissedCount},producerMissingUs={stream.ProducerMissingTimeUs},sinkMissed={stream.SinkMissedCount},sinkMissingUs={stream.SinkMissingTimeUs},backlog={stream.SinkBacklogBatches}"));
        return $"present=true;reason={status.SessionResultReason};stoppedUs={status.StoppedMonotonicDeltaUs};streams={streams}";
    }

    private void InitializeProfilingCaptureLocked(ulong sampleMonotonicUs)
    {
        if (!ProfilingBench01.ShouldStartCapture || sessionHeader is null || captureStartUtc is null)
        {
            return;
        }

        profilingCaptureCorrelationId = ProfilingRuntime.CreateCorrelationId();
        profilingResourceSampler = ProfilingResourceSampler.Start(
            ProfilingBench01.Scenario,
            profilingCaptureCorrelationId);
        profilingNextCheckpointIndex = 0;
        profilingFirstTravelIndex = null;
        profilingLastTravelIndex = null;
        ProfilingBench01.CaptureStarted(profilingCaptureCorrelationId);
        ProfilingRuntime.Marker(
            ProfilingBench01.Scenario,
            "CaptureStart",
            profilingCaptureCorrelationId,
            FormattableString.Invariant(
                $"identityKey={context.IdentityKey};sampleMonotonicUs={sampleMonotonicUs};captureUtcMs={captureStartUtc.Value.ToUnixTimeMilliseconds()};travelRateMhz={sessionHeader.AcceptedTravelRateMhz};imuRateMhz={sessionHeader.AcceptedImuRateMhz};gpsRateMhz={sessionHeader.AcceptedGpsRateMhz};temperatureRateMhz={sessionHeader.AcceptedTemperatureRateMhz};acceptedStreams={(uint)sessionHeader.AcceptedStreamMask};imuLocations={string.Join(',', sessionHeader.GetActiveImuLocations())}"));
        EmitProfilingCheckpointLocked("start", 0);
    }

    private void CompleteProfilingCaptureLocked(string reason)
    {
        if (profilingCaptureCorrelationId is null)
        {
            return;
        }

        var correlationId = profilingCaptureCorrelationId;
        ProfilingRuntime.Marker(
            ProfilingBench01.Scenario,
            "CaptureComplete",
            correlationId,
            FormattableString.Invariant(
                $"reason={reason};durationSeconds={CalculateCaptureDurationLocked().TotalSeconds:F6};firstIndex={profilingFirstTravelIndex?.ToString() ?? "none"};lastIndex={profilingLastTravelIndex?.ToString() ?? "none"}"));
        profilingResourceSampler?.Record("Capture.Resources", $"checkpoint={reason}");
        profilingResourceSampler?.Dispose();
        profilingResourceSampler = null;
        profilingCaptureCorrelationId = null;
        profilingNextCheckpointIndex = 0;
        profilingFirstTravelIndex = null;
        profilingLastTravelIndex = null;
        ProfilingBench01.CaptureCompleted(correlationId);
        ProfilingRuntime.Flush();
    }

    private void TryEmitProfilingTimeCheckpointsLocked()
    {
        if (profilingCaptureCorrelationId is null)
        {
            return;
        }

        var durationSeconds = CalculateCaptureDurationLocked().TotalSeconds;
        while (profilingNextCheckpointIndex < ProfilingCheckpointSeconds.Length &&
               durationSeconds >= ProfilingCheckpointSeconds[profilingNextCheckpointIndex])
        {
            var checkpointSeconds = ProfilingCheckpointSeconds[profilingNextCheckpointIndex++];
            EmitProfilingCheckpointLocked(
                $"t{checkpointSeconds}",
                durationSeconds,
                checkpointSeconds);
            if (checkpointSeconds == 120)
            {
                ProfilingBench01.CaptureReachedTarget(profilingCaptureCorrelationId);
            }
        }
    }

    private void EmitProfilingCheckpointLocked(
        string name,
        double observedDurationSeconds,
        double? exactPrefixSeconds = null)
    {
        if (profilingCaptureCorrelationId is null)
        {
            return;
        }

        var counts = exactPrefixSeconds is { } prefixSeconds
            ? CountCapturePrefixItemsLocked(prefixSeconds)
            : CountCurrentCaptureItemsLocked();
        var currentCount = CountCurrentCaptureItemsLocked().Total;
        var durationSeconds = exactPrefixSeconds ?? observedDurationSeconds;
        ProfilingRuntime.Checkpoint(
            ProfilingBench01.Scenario,
            $"Capture.{name}",
            profilingCaptureCorrelationId,
            counts.Total);
        ProfilingRuntime.Marker(
            ProfilingBench01.Scenario,
            "Capture.Load",
            profilingCaptureCorrelationId,
            FormattableString.Invariant(
                $"checkpoint={name};durationSeconds={durationSeconds:F6};observedDurationSeconds={observedDurationSeconds:F6};overrunItems={currentCount - counts.Total};firstIndex={profilingFirstTravelIndex?.ToString() ?? "none"};lastIndex={profilingLastTravelIndex?.ToString() ?? "none"};front={counts.Front};rear={counts.Rear};imu={string.Join(',', counts.Imu.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}:{entry.Value}"))};gps={counts.Gps};temperature={counts.Temperature};markers={counts.Markers};gaps={counts.Gaps};latestAnalysisRevision={latestAnalysisRevision};queuedAnalysisRevision={queuedAnalysisRevision};runningAnalysisRevision={runningAnalysisRevision};analysisSkipped={analysisRecomputesSkipped};signalBatchesCoalesced={signalBatchesCoalesced};signalSamplesDiscarded={signalSamplesDiscarded}"));
        profilingResourceSampler?.Record("Capture.Resources", $"checkpoint={name}");
        ProfilingRuntime.Flush();
    }

    private ProfilingCaptureCounts CountCurrentCaptureItemsLocked()
    {
        return new ProfilingCaptureCounts(
            Front: (long)frontTravelBuilder.Count,
            Rear: (long)rearTravelBuilder.Count,
            Imu: imuBuilders.ToDictionary(
                entry => entry.Key,
                entry => (long)entry.Value.Count),
            Gps: gpsRecords.Count,
            Temperature: temperatureSamples.Count,
            Markers: markers.Count,
            Gaps: streamGaps.Count);
    }

    private ProfilingCaptureCounts CountCapturePrefixItemsLocked(double prefixSeconds)
    {
        if (sessionHeader is null || captureStartMonotonicUs is null || captureStartUtc is null)
        {
            return CountCurrentCaptureItemsLocked();
        }

        var captureStartDeltaUs = captureStartMonotonicUs.Value >= sessionHeader.SessionStartMonotonicUs
            ? captureStartMonotonicUs.Value - sessionHeader.SessionStartMonotonicUs
            : 0;
        var prefixDurationUs = (ulong)Math.Round(
            prefixSeconds * 1_000_000.0,
            MidpointRounding.AwayFromZero);
        var prefixEndDeltaUs = checked(captureStartDeltaUs + prefixDurationUs);
        var prefixEndUtc = captureStartUtc.Value.AddSeconds(prefixSeconds);
        var imuCounts = imuBuilders.ToDictionary(
            entry => entry.Key,
            entry => CountFixedRatePrefix(
                entry.Value.CreateSnapshot(),
                sessionHeader.AcceptedImuRateMhz,
                captureStartDeltaUs,
                prefixDurationUs));

        var counts = new ProfilingCaptureCounts(
            Front: CountFixedRatePrefix(
                frontTravelBuilder.CreateSnapshot(),
                sessionHeader.AcceptedTravelRateMhz,
                captureStartDeltaUs,
                prefixDurationUs),
            Rear: CountFixedRatePrefix(
                rearTravelBuilder.CreateSnapshot(),
                sessionHeader.AcceptedTravelRateMhz,
                captureStartDeltaUs,
                prefixDurationUs),
            Imu: imuCounts,
            Gps: CountChunkedPrefix(
                gpsRecords.CreateSnapshot(),
                record =>
                {
                    var timestamp = new DateTimeOffset(record.Timestamp);
                    return timestamp > captureStartUtc.Value && timestamp <= prefixEndUtc;
                }),
            Temperature: CountChunkedPrefix(
                temperatureSamples.CreateSnapshot(),
                sample =>
                {
                    var timestamp = DateTimeOffset.FromUnixTimeSeconds(sample.TimestampUtc);
                    return timestamp > captureStartUtc.Value && timestamp <= prefixEndUtc;
                }),
            Markers: markers.LongCount(marker =>
                marker.TimestampOffset > captureStartDeltaUs / 1_000_000.0 &&
                marker.TimestampOffset <= prefixEndDeltaUs / 1_000_000.0),
            Gaps: streamGaps.Count);

        if (!ProfilingLiveDaqReplay.IsActive ||
            Math.Abs(prefixSeconds - ProfilingLiveDaqReplay.TargetSeconds) > 0.000_001)
        {
            return counts;
        }

        return new ProfilingCaptureCounts(
            Front: Math.Min(counts.Front, ProfilingLiveDaqReplay.TargetTravelSamples),
            Rear: Math.Min(counts.Rear, ProfilingLiveDaqReplay.TargetTravelSamples),
            Imu: counts.Imu.ToDictionary(
                entry => entry.Key,
                entry => Math.Min(
                    entry.Value,
                    ProfilingLiveDaqReplay.TargetImuSamplesPerLocation)),
            Gps: Math.Min(counts.Gps, ProfilingLiveDaqReplay.TargetGpsSamples),
            Temperature: Math.Min(
                counts.Temperature,
                ProfilingLiveDaqReplay.TargetTemperatureSamples),
            Markers: Math.Min(counts.Markers, ProfilingLiveDaqReplay.TargetMarkers),
            Gaps: counts.Gaps);
    }

    private static long CountFixedRatePrefix<T>(
        IEnumerable<FixedRateSegment<T>> segments,
        uint rateMhz,
        ulong captureStartDeltaUs,
        ulong prefixDurationUs)
    {
        var firstIndex = ScaleMicrosecondsToSampleIndexCeiling(
            captureStartDeltaUs,
            rateMhz);
        var endIndexExclusive = checked(
            firstIndex +
            ScaleMicrosecondsToSampleIndexCeiling(prefixDurationUs, rateMhz));
        long count = 0;
        foreach (var segment in segments)
        {
            var segmentEndIndex = checked(
                segment.FirstIndex +
                (ulong)segment.Values.Count);
            var keepStart = Math.Max(segment.FirstIndex, firstIndex);
            var keepEnd = Math.Min(segmentEndIndex, endIndexExclusive);
            if (keepEnd > keepStart)
            {
                count += checked((long)(keepEnd - keepStart));
            }
        }

        return count;
    }

    private static ulong ScaleMicrosecondsToSampleIndexCeiling(
        ulong microseconds,
        uint rateMhz)
    {
        const ulong scale = 1_000_000_000;
        var numerator = checked(microseconds * rateMhz);
        return checked((numerator + scale - 1) / scale);
    }

    private static long CountChunkedPrefix<T>(
        ChunkedBufferSnapshot<T> snapshot,
        Func<T, bool> include)
    {
        long count = 0;
        foreach (var chunk in snapshot.SealedChunks)
        {
            count += chunk.LongCount(include);
        }

        for (var index = 0; index < snapshot.ActiveCount; index++)
        {
            if (include(snapshot.ActiveChunk[index]))
            {
                count++;
            }
        }

        return count;
    }
#endif

    private static RawImuData? BuildImuCapture(LiveCaptureSnapshot snapshot)
    {
        if (snapshot.SessionHeader is null || snapshot.ImuSegmentsByLocation.Count == 0)
        {
            return null;
        }

        var activeLocations = snapshot.SessionHeader.GetActiveImuLocations();
        var imuData = new RawImuData
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
        };

        foreach (var location in activeLocations)
        {
            if (!snapshot.ImuSegmentsByLocation.TryGetValue(location, out var segments))
            {
                continue;
            }

            imuData.Segments.AddRange(segments.Select(segment => new RawImuSegment
            {
                LocationId = (byte)location,
                FirstIndex = segment.FirstIndex,
                FirstMonotonicDeltaUs = segment.FirstMonotonicDeltaUs,
                Records = segment.Values.ToArray(),
            }));
        }

        if (imuData.Segments.Count == 0)
        {
            return null;
        }

        RawImuDataSegmentHelper.FinalizeCanonicalSegments(imuData, snapshot.StreamGaps);
        return imuData;
    }

    private static RawCountSegment[] ToRawCountSegments(FixedRateSegment<ushort>[] segments) =>
        segments.Select(segment => new RawCountSegment
        {
            FirstIndex = segment.FirstIndex,
            FirstMonotonicDeltaUs = segment.FirstMonotonicDeltaUs,
            Counts = segment.Values.ToArray(),
        }).ToArray();

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

        if (finalStatus is not null && captureStartMonotonicUs is not null && sessionHeader is not null)
        {
            var captureStartDeltaUs = captureStartMonotonicUs.Value >= sessionHeader.SessionStartMonotonicUs
                ? captureStartMonotonicUs.Value - sessionHeader.SessionStartMonotonicUs
                : 0;
            var durationUs = finalStatus.StoppedMonotonicDeltaUs >= captureStartDeltaUs
                ? finalStatus.StoppedMonotonicDeltaUs - captureStartDeltaUs
                : 0;
            return TimeSpan.FromMilliseconds(durationUs / 1000.0);
        }

        var travelEndDeltaUs = GetLatestTravelEndMonotonicDeltaUs();
        if (travelEndDeltaUs is not null && captureStartMonotonicUs is not null && sessionHeader is not null)
        {
            var captureStartDeltaUs = captureStartMonotonicUs.Value >= sessionHeader.SessionStartMonotonicUs
                ? captureStartMonotonicUs.Value - sessionHeader.SessionStartMonotonicUs
                : 0;
            var durationUs = travelEndDeltaUs.Value >= captureStartDeltaUs
                ? travelEndDeltaUs.Value - captureStartDeltaUs
                : 0;
            return TimeSpan.FromMilliseconds(durationUs / 1000.0);
        }

        if (sessionTrackPoints.Length > 0)
        {
            var duration = TimeSpan.FromSeconds(
                sessionTrackPoints[^1].Time - captureStartUtc.Value.ToUnixTimeSeconds());
            return duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        }

        return TimeSpan.Zero;
    }

    private ulong? GetLatestTravelEndMonotonicDeltaUs()
    {
        if (sessionHeader is null || sessionHeader.AcceptedTravelRateMhz == 0)
        {
            return null;
        }

        var latestEndMonotonicDeltaUs = frontTravelBuilder.LatestEndMonotonicDeltaUs;
        if (rearTravelBuilder.LatestEndMonotonicDeltaUs is ulong rearEndMonotonicDeltaUs &&
            (latestEndMonotonicDeltaUs is null || rearEndMonotonicDeltaUs > latestEndMonotonicDeltaUs.Value))
        {
            latestEndMonotonicDeltaUs = rearEndMonotonicDeltaUs;
        }

        return latestEndMonotonicDeltaUs;
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
#if SUFNI_PROFILING_DIAGNOSTICS
        InitializeProfilingCaptureLocked(sampleMonotonicUs);
#endif
    }

    private bool CanSaveLocked()
    {
        return frontTravelBuilder.Count >= 5 || rearTravelBuilder.Count >= 5;
    }

    private bool CanBuildAnalysisLocked()
    {
        return CanSaveLocked() && sessionHeader is not null && sessionHeader.AcceptedTravelHz > 0;
    }

    private bool HasAnyCaptureLocked()
    {
        return frontTravelBuilder.Count > 0 ||
            rearTravelBuilder.Count > 0 ||
            imuBuilders.Values.Any(builder => builder.Count > 0) ||
            gpsRecords.Count > 0 ||
            temperatureSamples.Count > 0 ||
            markers.Count > 0;
    }

    private void ThrowIfDisposed()
    {
        if (isDisposed)
        {
            throw new ObjectDisposedException(nameof(LiveSessionService));
        }
    }
}

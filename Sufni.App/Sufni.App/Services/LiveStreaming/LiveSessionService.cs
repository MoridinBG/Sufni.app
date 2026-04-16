using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Queries;
using Sufni.App.Services;
using Sufni.App.SessionDetails;
using Sufni.App.ViewModels.Editors;
using Sufni.Telemetry;
using Serilog;

namespace Sufni.App.Services.LiveStreaming;

internal sealed class LiveSessionServiceFactory(
    ISessionPresentationService sessionPresentationService,
    IBackgroundTaskRunner backgroundTaskRunner) : ILiveSessionServiceFactory
{
    public ILiveSessionService Create(LiveDaqSessionContext context, ILiveDaqSharedStream sharedStream)
    {
        return new LiveSessionService(context, sharedStream, sessionPresentationService, backgroundTaskRunner);
    }
}

internal sealed class LiveSessionService : ILiveSessionService
{
    private const int MaxVelocityWindowSamples = 127;
    private const int MeasurementChunkSize = 4096;
    private const int ImuChunkSize = 2048;
    private const int GpsChunkSize = 256;

    private static readonly ILogger logger = Log.ForContext<LiveSessionService>();

    private readonly record struct LiveCaptureSnapshot(
        Metadata Metadata,
        LiveSessionHeader? SessionHeader,
        ChunkedBufferSnapshot<ushort> FrontMeasurements,
        ChunkedBufferSnapshot<ushort> RearMeasurements,
        ChunkedBufferSnapshot<ImuRecord> ImuRecords,
        ChunkedBufferSnapshot<GpsRecord> GpsRecords);

    private readonly LiveDaqSessionContext context;
    private readonly ILiveDaqSharedStream sharedStream;
    private readonly ISessionPresentationService sessionPresentationService;
    private readonly IBackgroundTaskRunner backgroundTaskRunner;
    private readonly object gate = new();
    private readonly BehaviorSubject<LiveSessionPresentationSnapshot> snapshotsSubject = new(LiveSessionPresentationSnapshot.Empty);
    private readonly Subject<LiveGraphBatch> graphBatchesSubject = new();

    private readonly AppendOnlyChunkBuffer<ushort> frontMeasurements = new(MeasurementChunkSize);
    private readonly AppendOnlyChunkBuffer<ushort> rearMeasurements = new(MeasurementChunkSize);
    private readonly AppendOnlyChunkBuffer<ImuRecord> imuRecords = new(ImuChunkSize);
    private readonly AppendOnlyChunkBuffer<GpsRecord> gpsRecords = new(GpsChunkSize);
    private readonly List<double> recentTravelTimes = [];
    private readonly List<double> recentFrontTravel = [];
    private readonly List<double> recentRearTravel = [];

    private IDisposable? framesSubscription;
    private IDisposable? statesSubscription;
    private ILiveDaqSharedStreamLease? observerLease;
    private ILiveDaqSharedStreamLease? configurationLockLease;
    private LiveSessionPresentationSnapshot current = LiveSessionPresentationSnapshot.Empty;
    private LiveSessionHeader? sessionHeader;
    private LiveSessionStats? latestSessionStats;
    private TelemetryData? statisticsTelemetry;
    private SessionDamperPercentages damperPercentages = new(null, null, null, null, null, null, null, null);
    private TrackPoint[] sessionTrackPoints = [];
    private LiveConnectionState connectionState = LiveConnectionState.Disconnected;
    private string? lastError;
    private ulong? captureStartMonotonicUs;
    private DateTimeOffset? captureStartUtc;
    private long captureRevision;
    private long latestStatisticsRevision = -1;
    private long queuedStatisticsRevision = -1;
    private bool statisticsRunning;
    private bool hasPublishedSaveableCapture;
    private bool isTerminalClosed;
    private bool isAttached;
    private bool isDisposed;
    private DateTimeOffset nextStatisticsRunAt = DateTimeOffset.MinValue;

    public LiveSessionService(
        LiveDaqSessionContext context,
        ILiveDaqSharedStream sharedStream,
        ISessionPresentationService sessionPresentationService,
        IBackgroundTaskRunner backgroundTaskRunner)
    {
        this.context = context;
        this.sharedStream = sharedStream;
        this.sessionPresentationService = sessionPresentationService;
        this.backgroundTaskRunner = backgroundTaskRunner;
    }

    public IObservable<LiveSessionPresentationSnapshot> Snapshots => snapshotsSubject.AsObservable();

    public IObservable<LiveGraphBatch> GraphBatches => graphBatchesSubject.AsObservable();

    public LiveSessionPresentationSnapshot Current => current;

    public async Task EnsureAttachedAsync(CancellationToken cancellationToken = default)
    {
        bool shouldStart;

        lock (gate)
        {
            ThrowIfDisposed();
            if (!isAttached)
            {
                observerLease = sharedStream.AcquireLease();
                configurationLockLease = sharedStream.AcquireConfigurationLock();
                framesSubscription = sharedStream.Frames.Subscribe(HandleFrame);
                statesSubscription = sharedStream.States.Subscribe(HandleSharedStreamState);
                isAttached = true;
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

    public Task ResetCaptureAsync(CancellationToken cancellationToken = default)
    {
        LiveGraphBatch resetBatch;
        LiveSessionPresentationSnapshot snapshot;
        lock (gate)
        {
            ThrowIfDisposed();
            frontMeasurements.Clear();
            rearMeasurements.Clear();
            imuRecords.Clear();
            gpsRecords.Clear();
            recentTravelTimes.Clear();
            recentFrontTravel.Clear();
            recentRearTravel.Clear();
            statisticsTelemetry = null;
            damperPercentages = new SessionDamperPercentages(null, null, null, null, null, null, null, null);
            sessionTrackPoints = [];
            captureStartMonotonicUs = null;
            captureStartUtc = null;
            captureRevision++;
            latestStatisticsRevision = captureRevision;
            queuedStatisticsRevision = -1;
            nextStatisticsRunAt = DateTimeOffset.MinValue;
            hasPublishedSaveableCapture = false;
            resetBatch = LiveGraphBatch.Empty with { Revision = captureRevision };
            snapshot = BuildSnapshotLocked();
        }

        graphBatchesSubject.OnNext(resetBatch);
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
            framesSubscription = null;
            statesSubscription = null;
            configurationLockLease = null;
            observerLease = null;
        }

        frames?.Dispose();
        states?.Dispose();

        if (configurationLock is not null)
        {
            await configurationLock.DisposeAsync();
        }

        if (observer is not null)
        {
            await observer.DisposeAsync();
        }

        snapshotsSubject.OnCompleted();
        graphBatchesSubject.OnCompleted();
        snapshotsSubject.Dispose();
        graphBatchesSubject.Dispose();
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
        LiveGraphBatch? graphBatch = null;
        LiveSessionPresentationSnapshot? snapshotToPublish = null;
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
                    graphBatch = ApplyTravelBatchLocked(travelBatchFrame);
                    shouldQueueStatistics = CanBuildStatisticsLocked();
                    if (CanSaveLocked() && !hasPublishedSaveableCapture)
                    {
                        hasPublishedSaveableCapture = true;
                        snapshotToPublish = BuildSnapshotLocked();
                    }
                    break;

                case LiveImuBatchFrame imuBatchFrame:
                    graphBatch = ApplyImuBatchLocked(imuBatchFrame);
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

        if (graphBatch is not null)
        {
            graphBatchesSubject.OnNext(graphBatch);
        }

        if (snapshotToPublish is not null)
        {
            PublishSnapshot(snapshotToPublish);
        }

        if (shouldQueueStatistics)
        {
            QueueStatisticsRecompute();
        }
    }

    private LiveGraphBatch? ApplyTravelBatchLocked(LiveTravelBatchFrame frame)
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

            AppendRecentTravelSample(timeOffset, frontValue, rearValue);
        }

        captureRevision++;

        return new LiveGraphBatch(
            Revision: captureRevision,
            TravelTimes: travelTimes,
            FrontTravel: frontTravel,
            RearTravel: rearTravel,
            VelocityTimes: travelTimes,
            FrontVelocity: ComputeVelocityAppend(recentTravelTimes, recentFrontTravel, batchCount),
            RearVelocity: ComputeVelocityAppend(recentTravelTimes, recentRearTravel, batchCount),
            ImuTimes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>(),
            ImuMagnitudes: new Dictionary<LiveImuLocation, IReadOnlyList<double>>());
    }

    private LiveGraphBatch? ApplyImuBatchLocked(LiveImuBatchFrame frame)
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

        var imuTimes = new Dictionary<LiveImuLocation, List<double>>();
        var imuMagnitudes = new Dictionary<LiveImuLocation, List<double>>();
        foreach (var location in activeLocations)
        {
            imuTimes[location] = [];
            imuMagnitudes[location] = [];
        }

        var tickCount = (int)frame.Batch.SampleCount;
        var recordsPerTick = activeLocations.Count;
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
                imuTimes[location].Add(timeOffset);
                imuMagnitudes[location].Add(
                    ConvertImuMagnitude(frame.Records[recordIndex], location, sessionHeader.ImuCalibrationScales));
            }
        }

        return new LiveGraphBatch(
            Revision: captureRevision,
            TravelTimes: [],
            FrontTravel: [],
            RearTravel: [],
            VelocityTimes: [],
            FrontVelocity: [],
            RearVelocity: [],
            ImuTimes: imuTimes.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<double>)entry.Value),
            ImuMagnitudes: imuMagnitudes.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<double>)entry.Value));
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

    private void QueueStatisticsRecompute()
    {
        bool shouldStart;
        lock (gate)
        {
            if (!CanBuildStatisticsLocked() || isDisposed)
            {
                return;
            }

            queuedStatisticsRevision = captureRevision;
            shouldStart = !statisticsRunning;
            if (shouldStart)
            {
                statisticsRunning = true;
            }
        }

        if (shouldStart)
        {
            _ = RunStatisticsLoopAsync();
        }
    }

    private async Task RunStatisticsLoopAsync()
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
                    statisticsRunning = false;
                    return;
                }

                var now = DateTimeOffset.UtcNow;
                delay = nextStatisticsRunAt > now ? nextStatisticsRunAt - now : TimeSpan.Zero;
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay);
            }

            lock (gate)
            {
                if (isDisposed || !CanBuildStatisticsLocked() || queuedStatisticsRevision <= latestStatisticsRevision)
                {
                    statisticsRunning = false;
                    return;
                }

                revision = queuedStatisticsRevision;
                capture = CreateCaptureSnapshotLocked();
                nextStatisticsRunAt = DateTimeOffset.UtcNow.AddMilliseconds(LiveSessionRefreshCadence.StatisticsRefreshIntervalMs);
            }

            try
            {
                var telemetryData = await backgroundTaskRunner.RunAsync(
                    () => TelemetryData.FromLiveCapture(BuildCapture(capture)),
                    CancellationToken.None);
                var percentages = sessionPresentationService.CalculateDamperPercentages(telemetryData);

                LiveSessionPresentationSnapshot snapshot;
                bool shouldContinue;
                lock (gate)
                {
                    if (revision >= latestStatisticsRevision)
                    {
                        statisticsTelemetry = telemetryData;
                        damperPercentages = percentages;
                        latestStatisticsRevision = revision;
                    }

                    snapshot = BuildSnapshotLocked();
                    shouldContinue = queuedStatisticsRevision > revision;
                    if (!shouldContinue)
                    {
                        statisticsRunning = false;
                    }
                }

                PublishSnapshot(snapshot);

                if (!shouldContinue)
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Live session statistics recompute failed for {IdentityKey}", context.IdentityKey);

                lock (gate)
                {
                    if (queuedStatisticsRevision <= revision)
                    {
                        statisticsRunning = false;
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
            CanSave: CanSaveLocked(),
            IsSaving: false,
            IsResetting: false);
    }

    private LiveCaptureSnapshot CreateCaptureSnapshotLocked()
    {
        return new LiveCaptureSnapshot(
            Metadata: new Metadata
            {
                SourceName = context.DisplayName,
                Version = 4,
                SampleRate = (int)(sessionHeader?.AcceptedTravelHz ?? 0),
                Timestamp = (int)((captureStartUtc ?? sessionHeader?.SessionStartUtc ?? DateTimeOffset.UnixEpoch).ToUnixTimeSeconds()),
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

    private void AppendRecentTravelSample(double timeOffset, double frontTravel, double rearTravel)
    {
        recentTravelTimes.Add(timeOffset);
        recentFrontTravel.Add(frontTravel);
        recentRearTravel.Add(rearTravel);

        while (recentTravelTimes.Count > MaxVelocityWindowSamples)
        {
            recentTravelTimes.RemoveAt(0);
            recentFrontTravel.RemoveAt(0);
            recentRearTravel.RemoveAt(0);
        }
    }

    private static double[] ComputeVelocityAppend(IReadOnlyList<double> times, IReadOnlyList<double> travel, int batchCount)
    {
        if (times.Count < 5 || travel.Any(double.IsNaN))
        {
            return Enumerable.Repeat(double.NaN, batchCount).ToArray();
        }

        // This bounded recompute still allocates and reprocesses the trailing window on each append.
        // Keep it unchanged for now; the larger wins are the shared-stream lifecycle fix and the
        // off-lock full-capture snapshot refactor.
        var filterWindow = Math.Min(51, times.Count);
        if (filterWindow % 2 == 0)
        {
            filterWindow--;
        }

        var filter = SavitzkyGolay.Create(filterWindow, 1, 3);
        var velocities = filter.Process(travel.ToArray(), times.ToArray());
        return velocities[^batchCount..];
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
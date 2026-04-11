using System;
using System.Collections.Generic;
using Sufni.Telemetry;

namespace Sufni.App.Services.LiveStreaming;

public sealed class LiveDaqSessionState
{
    private readonly object gate = new();
    private readonly Dictionary<LiveImuLocation, LiveImuReading> latestImuReadings = [];

    private LiveSessionHeader? sessionHeader;
    private LiveSessionStats? sessionStats;
    private LiveTravelRecord? latestTravel;
    private ulong? latestTravelMonotonicUs;
    private GpsRecord? latestGps;
    private DateTimeOffset? lastFrameReceivedUtc;
    private uint travelQueueDepth;
    private uint imuQueueDepth;
    private uint gpsQueueDepth;
    private uint travelDroppedBatches;
    private uint imuDroppedBatches;
    private uint gpsDroppedBatches;

    public void Reset()
    {
        lock (gate)
        {
            sessionHeader = null;
            sessionStats = null;
            latestTravel = null;
            latestTravelMonotonicUs = null;
            latestGps = null;
            lastFrameReceivedUtc = null;
            travelQueueDepth = 0;
            imuQueueDepth = 0;
            gpsQueueDepth = 0;
            travelDroppedBatches = 0;
            imuDroppedBatches = 0;
            gpsDroppedBatches = 0;
            latestImuReadings.Clear();
        }
    }

    public void ApplyFrame(LiveProtocolFrame frame)
    {
        lock (gate)
        {
            lastFrameReceivedUtc = DateTimeOffset.UtcNow;

            switch (frame)
            {
                case LiveSessionHeaderFrame sessionHeaderFrame:
                    sessionHeader = sessionHeaderFrame.Payload;
                    break;

                case LiveTravelBatchFrame travelBatchFrame:
                    ApplyTravelBatch(travelBatchFrame);
                    break;

                case LiveImuBatchFrame imuBatchFrame:
                    ApplyImuBatch(imuBatchFrame);
                    break;

                case LiveGpsBatchFrame gpsBatchFrame:
                    ApplyGpsBatch(gpsBatchFrame);
                    break;

                case LiveSessionStatsFrame sessionStatsFrame:
                    sessionStats = sessionStatsFrame.Payload;
                    travelQueueDepth = sessionStatsFrame.Payload.TravelQueueDepth;
                    imuQueueDepth = sessionStatsFrame.Payload.ImuQueueDepth;
                    gpsQueueDepth = sessionStatsFrame.Payload.GpsQueueDepth;
                    travelDroppedBatches = sessionStatsFrame.Payload.TravelDroppedBatches;
                    imuDroppedBatches = sessionStatsFrame.Payload.ImuDroppedBatches;
                    gpsDroppedBatches = sessionStatsFrame.Payload.GpsDroppedBatches;
                    break;
            }
        }
    }

    public LiveDaqUiSnapshot CreateSnapshot(LiveConnectionState connectionState, string? lastError)
    {
        lock (gate)
        {
            var session = CreateSessionSnapshot();
            return new LiveDaqUiSnapshot(
                ConnectionState: connectionState,
                ConnectionStateText: LiveDaqUiSnapshot.ToConnectionStateText(connectionState),
                LastError: lastError,
                LastFrameReceivedUtc: lastFrameReceivedUtc,
                Session: session,
                Travel: CreateTravelSnapshot(session),
                Imus: CreateImuSnapshots(),
                Gps: CreateGpsSnapshot(session));
        }
    }

    private void ApplyTravelBatch(LiveTravelBatchFrame frame)
    {
        travelQueueDepth = frame.Batch.QueueDepth;
        travelDroppedBatches = frame.Batch.DroppedBatches;
        if (frame.Records.Count == 0)
        {
            return;
        }

        latestTravel = frame.Records[^1];
        latestTravelMonotonicUs = GetLastSampleMonotonicUs(frame.Batch.FirstMonotonicUs, frame.Batch.SampleCount, sessionHeader?.TravelPeriodUs);
    }

    private void ApplyImuBatch(LiveImuBatchFrame frame)
    {
        imuQueueDepth = frame.Batch.QueueDepth;
        imuDroppedBatches = frame.Batch.DroppedBatches;
        if (frame.Records.Count == 0 || sessionHeader is null)
        {
            return;
        }

        var activeLocations = sessionHeader.GetActiveImuLocations();
        if (activeLocations.Count == 0 || frame.Records.Count < activeLocations.Count)
        {
            return;
        }

        var lastTickOffset = frame.Records.Count - activeLocations.Count;
        var sampleMonotonicUs = GetLastSampleMonotonicUs(frame.Batch.FirstMonotonicUs, frame.Batch.SampleCount, sessionHeader.ImuPeriodUs);
        for (var index = 0; index < activeLocations.Count; index++)
        {
            latestImuReadings[activeLocations[index]] = new LiveImuReading(frame.Records[lastTickOffset + index], sampleMonotonicUs);
        }
    }

    private void ApplyGpsBatch(LiveGpsBatchFrame frame)
    {
        gpsQueueDepth = frame.Batch.QueueDepth;
        gpsDroppedBatches = frame.Batch.DroppedBatches;
        if (frame.Records.Count == 0)
        {
            return;
        }

        latestGps = frame.Records[^1];
    }

    private LiveSessionContractSnapshot CreateSessionSnapshot()
    {
        if (sessionHeader is null)
        {
            return LiveSessionContractSnapshot.Empty;
        }

        var stats = sessionStats;
        return new LiveSessionContractSnapshot(
            SessionId: sessionHeader.SessionId,
            SelectedSensorMask: sessionHeader.SelectedSensorMask,
            PublishCadenceMs: sessionHeader.PublishCadenceMs,
            AcceptedTravelHz: stats?.AcceptedTravelHz ?? sessionHeader.AcceptedTravelHz,
            AcceptedImuHz: stats?.AcceptedImuHz ?? sessionHeader.AcceptedImuHz,
            AcceptedGpsFixHz: stats?.AcceptedGpsFixHz ?? sessionHeader.AcceptedGpsFixHz,
            TravelPeriodUs: stats?.TravelPeriodUs ?? sessionHeader.TravelPeriodUs,
            ImuPeriodUs: stats?.ImuPeriodUs ?? sessionHeader.ImuPeriodUs,
            GpsFixIntervalMs: stats?.GpsFixIntervalMs ?? sessionHeader.GpsFixIntervalMs,
            SessionStartUtc: sessionHeader.SessionStartUtc,
            TravelQueueCapacity: sessionHeader.TravelQueueCapacity,
            ImuQueueCapacity: sessionHeader.ImuQueueCapacity,
            GpsQueueCapacity: sessionHeader.GpsQueueCapacity,
            Flags: sessionHeader.Flags,
            ActiveImuLocations: sessionHeader.GetActiveImuLocations());
    }

    private LiveTravelUiSnapshot CreateTravelSnapshot(LiveSessionContractSnapshot session)
    {
        var isActive = session.SelectedSensorMask.HasFlag(LiveSensorMask.Travel);
        if (latestTravel is not LiveTravelRecord travel)
        {
            return LiveTravelUiSnapshot.Empty with
            {
                IsActive = isActive,
                QueueDepth = travelQueueDepth,
                DroppedBatches = travelDroppedBatches,
            };
        }

        return new LiveTravelUiSnapshot(
            IsActive: isActive,
            HasData: true,
            FrontTravel: travel.ForkAngle,
            RearTravel: travel.ShockAngle,
            SampleOffset: CreateSampleOffset(latestTravelMonotonicUs),
            QueueDepth: travelQueueDepth,
            DroppedBatches: travelDroppedBatches);
    }

    private IReadOnlyList<LiveImuUiSnapshot> CreateImuSnapshots()
    {
        if (sessionHeader is null)
        {
            return [];
        }

        var snapshots = new List<LiveImuUiSnapshot>();
        foreach (var location in sessionHeader.GetActiveImuLocations())
        {
            if (!latestImuReadings.TryGetValue(location, out var reading))
            {
                snapshots.Add(new LiveImuUiSnapshot(
                    Location: location,
                    HasData: false,
                    Ax: null,
                    Ay: null,
                    Az: null,
                    Gx: null,
                    Gy: null,
                    Gz: null,
                    SampleOffset: null,
                    QueueDepth: imuQueueDepth,
                    DroppedBatches: imuDroppedBatches));
                continue;
            }

            snapshots.Add(new LiveImuUiSnapshot(
                Location: location,
                HasData: true,
                Ax: reading.Record.Ax,
                Ay: reading.Record.Ay,
                Az: reading.Record.Az,
                Gx: reading.Record.Gx,
                Gy: reading.Record.Gy,
                Gz: reading.Record.Gz,
                SampleOffset: CreateSampleOffset(reading.MonotonicUs),
                QueueDepth: imuQueueDepth,
                DroppedBatches: imuDroppedBatches));
        }

        return snapshots;
    }

    private LiveGpsUiSnapshot CreateGpsSnapshot(LiveSessionContractSnapshot session)
    {
        var isActive = session.SelectedSensorMask.HasFlag(LiveSensorMask.Gps);
        if (latestGps is null)
        {
            return LiveGpsUiSnapshot.Empty with
            {
                IsActive = isActive,
                PreviewState = GpsPreviewState.NoFix,
                QueueDepth = gpsQueueDepth,
                DroppedBatches = gpsDroppedBatches,
            };
        }

        return new LiveGpsUiSnapshot(
            IsActive: isActive,
            HasData: true,
            PreviewState: GpsPreviewState.FromRecord(latestGps),
            FixTimestampUtc: latestGps.Timestamp.ToUniversalTime(),
            Latitude: latestGps.Latitude,
            Longitude: latestGps.Longitude,
            Altitude: latestGps.Altitude,
            Speed: latestGps.Speed,
            Heading: latestGps.Heading,
            Satellites: latestGps.Satellites,
            Epe2d: latestGps.Epe2d,
            Epe3d: latestGps.Epe3d,
            QueueDepth: gpsQueueDepth,
            DroppedBatches: gpsDroppedBatches);
    }

    private TimeSpan? CreateSampleOffset(ulong? monotonicUs)
    {
        if (monotonicUs is null || sessionHeader is null)
        {
            return null;
        }

        var deltaUs = monotonicUs.Value >= sessionHeader.SessionStartMonotonicUs
            ? monotonicUs.Value - sessionHeader.SessionStartMonotonicUs
            : 0;
        return TimeSpan.FromMilliseconds(deltaUs / 1000.0);
    }

    private static ulong GetLastSampleMonotonicUs(ulong firstMonotonicUs, uint sampleCount, uint? periodUs)
    {
        if (sampleCount == 0 || periodUs is null)
        {
            return firstMonotonicUs;
        }

        return firstMonotonicUs + ((ulong)sampleCount - 1) * periodUs.Value;
    }

    private readonly record struct LiveImuReading(ImuRecord Record, ulong MonotonicUs);
}
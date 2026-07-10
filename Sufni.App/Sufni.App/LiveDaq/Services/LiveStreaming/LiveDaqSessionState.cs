using System;
using System.Collections.Generic;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

// Lock-protected accumulator for the latest live session contract, sensor readings,
// and link-health counters. The view model reads it as snapshot state on a timer.
public sealed class LiveDaqSessionState
{
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<LiveImuLocation, LiveImuReading> latestImuReadings = [];
    private readonly Dictionary<LiveImuLocation, LiveTemperatureReading> latestTemperatureReadings = [];

    private LiveStreamMask selectedStreamMask;
    private LiveSessionHeader? sessionHeader;
    private LiveTravelRecord? latestTravel;
    private ulong? latestTravelMonotonicUs;
    private GpsRecord? latestGps;
    private DateTimeOffset? lastFrameReceivedUtc;
    private DateTimeOffset? lastTravelBatchReceivedUtc;
    private DateTimeOffset? lastImuBatchReceivedUtc;
    private DateTimeOffset? lastTemperatureBatchReceivedUtc;
    private uint travelQueueDepth;
    private uint imuQueueDepth;
    private uint gpsQueueDepth;
    private uint travelDroppedBatches;
    private uint imuDroppedBatches;
    private uint gpsDroppedBatches;

    // Clears the current session contract and all cached sensor readings.
    public void Reset()
    {
        lock (gate)
        {
            selectedStreamMask = LiveStreamMask.None;
            sessionHeader = null;
            latestTravel = null;
            latestTravelMonotonicUs = null;
            latestGps = null;
            lastFrameReceivedUtc = null;
            lastTravelBatchReceivedUtc = null;
            lastImuBatchReceivedUtc = null;
            lastTemperatureBatchReceivedUtc = null;
            travelQueueDepth = 0;
            imuQueueDepth = 0;
            gpsQueueDepth = 0;
            travelDroppedBatches = 0;
            imuDroppedBatches = 0;
            gpsDroppedBatches = 0;
            latestImuReadings.Clear();
            latestTemperatureReadings.Clear();
        }
    }

    public void ApplySharedSessionState(LiveSessionHeader? nextSessionHeader, LiveStreamMask nextSelectedStreamMask)
    {
        lock (gate)
        {
            sessionHeader = nextSessionHeader;
            selectedStreamMask = nextSelectedStreamMask;
        }
    }

    // Applies one parsed live protocol frame to the cached session state.
    public void ApplyFrame(LiveProtocolFrame frame)
    {
        lock (gate)
        {
            lastFrameReceivedUtc = DateTimeOffset.UtcNow;

            switch (frame)
            {
                case LiveTravelBatchFrame travelBatchFrame:
                    ApplyTravelBatch(travelBatchFrame);
                    break;

                case LiveImuBatchFrame imuBatchFrame:
                    ApplyImuBatch(imuBatchFrame);
                    break;

                case LiveGpsBatchFrame gpsBatchFrame:
                    ApplyGpsBatch(gpsBatchFrame);
                    break;

                case LiveTemperatureBatchFrame temperatureBatchFrame:
                    ApplyTemperatureBatch(temperatureBatchFrame);
                    break;

                case LiveSessionStatsFrame sessionStatsFrame:
                    ApplySessionStats(sessionStatsFrame.Payload);
                    break;

                case LiveStatusFrame statusFrame:
                    ApplySessionStats(LiveProtocolHelpers.CreateSessionStatsFromStatus(statusFrame.Streams));
                    break;

                case LiveBatteryBatchFrame:
                    break;
            }
        }
    }

    // Creates one coherent UI snapshot from the latest cached state and supplied
    // connection or error information.
    public LiveDaqUiSnapshot CreateSnapshot(LiveConnectionState connectionState, string? lastError)
    {
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            var session = CreateSessionSnapshot();
            return new LiveDaqUiSnapshot(
                ConnectionState: connectionState,
                ConnectionStateText: LiveDaqUiSnapshot.ToConnectionStateText(connectionState),
                LastError: lastError,
                LastFrameReceivedUtc: lastFrameReceivedUtc,
                Session: session,
                Travel: CreateTravelSnapshot(session, now),
                Imus: CreateImuSnapshots(now),
                Gps: CreateGpsSnapshot(session))
            {
                Temperatures = CreateTemperatureSnapshots(now),
            };
        }
    }

    private void ApplyTravelBatch(LiveTravelBatchFrame frame)
    {
        if (frame.Records.Count == 0)
        {
            return;
        }

        latestTravel = frame.Records[^1];
        latestTravelMonotonicUs = GetLastSampleMonotonicUs(frame.Batch.FirstMonotonicUs, frame.Batch.SampleCount, sessionHeader?.TravelPeriodUs);
        lastTravelBatchReceivedUtc = lastFrameReceivedUtc;
    }

    private void ApplyImuBatch(LiveImuBatchFrame frame)
    {
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
        lastImuBatchReceivedUtc = lastFrameReceivedUtc;
    }

    private void ApplyGpsBatch(LiveGpsBatchFrame frame)
    {
        if (frame.Records.Count == 0)
        {
            return;
        }

        latestGps = frame.Records[^1];
    }

    private void ApplyTemperatureBatch(LiveTemperatureBatchFrame frame)
    {
        if (frame.Records.Count == 0 || sessionHeader is null)
        {
            return;
        }

        foreach (var record in frame.Records)
        {
            var location = (LiveImuLocation)record.Sample.LocationId;
            latestTemperatureReadings[location] = new LiveTemperatureReading(
                record.Sample,
                checked(sessionHeader.SessionStartMonotonicUs + record.MonotonicDeltaUs));
        }
        lastTemperatureBatchReceivedUtc = lastFrameReceivedUtc;
    }

    private void ApplySessionStats(LiveSessionStats stats)
    {
        travelQueueDepth = stats.TravelQueueDepth;
        imuQueueDepth = stats.ImuQueueDepth;
        gpsQueueDepth = stats.GpsQueueDepth;
        travelDroppedBatches = stats.TravelDroppedBatches;
        imuDroppedBatches = stats.ImuDroppedBatches;
        gpsDroppedBatches = stats.GpsDroppedBatches;
    }

    private LiveSessionContractSnapshot CreateSessionSnapshot()
    {
        if (sessionHeader is null)
        {
            return LiveSessionContractSnapshot.Empty;
        }

        return new LiveSessionContractSnapshot(
            SessionId: sessionHeader.SessionId,
            SelectedStreamMask: selectedStreamMask,
            RequestedSensorMask: sessionHeader.RequestedSensorMask,
            AcceptedSensorMask: sessionHeader.AcceptedSensorMask,
            AcceptedTravelHz: sessionHeader.AcceptedTravelHz,
            AcceptedImuHz: sessionHeader.AcceptedImuHz,
            AcceptedGpsFixHz: sessionHeader.AcceptedGpsFixHz,
            SessionStartUtc: sessionHeader.SessionStartUtc,
            Flags: sessionHeader.Flags,
            ActiveImuLocations: sessionHeader.GetActiveImuLocations(),
            AcceptedTravelRateMhz: sessionHeader.AcceptedTravelRateMhz,
            AcceptedImuRateMhz: sessionHeader.AcceptedImuRateMhz,
            AcceptedGpsRateMhz: sessionHeader.AcceptedGpsRateMhz)
        {
            AcceptedTemperatureRateMhz = sessionHeader.AcceptedTemperatureRateMhz,
        };
    }

    private LiveTravelUiSnapshot CreateTravelSnapshot(LiveSessionContractSnapshot session, DateTimeOffset now)
    {
        var acceptedTravelMask = session.AcceptedSensorMask & LiveSensorInstanceMask.Travel;
        var isActive = acceptedTravelMask != LiveSensorInstanceMask.None;
        var frontIsActive = acceptedTravelMask.HasFlag(LiveSensorInstanceMask.ForkTravel);
        var rearIsActive = acceptedTravelMask.HasFlag(LiveSensorInstanceMask.ShockTravel);
        if (latestTravel is not LiveTravelRecord travel)
        {
            return LiveTravelUiSnapshot.Empty with
            {
                IsActive = isActive,
                FrontIsActive = frontIsActive,
                RearIsActive = rearIsActive,
                QueueDepth = travelQueueDepth,
                DroppedBatches = travelDroppedBatches,
            };
        }

        var sampleOffset = CreateSampleOffset(latestTravelMonotonicUs);
        var frontMeasurement = frontIsActive ? travel.ForkAngle : (ushort?)null;
        var rearMeasurement = rearIsActive ? travel.ShockAngle : (ushort?)null;
        return new LiveTravelUiSnapshot(
            IsActive: isActive,
            FrontIsActive: frontIsActive,
            RearIsActive: rearIsActive,
            HasData: frontMeasurement is not null || rearMeasurement is not null,
            FrontMeasurement: frontMeasurement,
            RearMeasurement: rearMeasurement,
            SampleOffset: sampleOffset,
            SampleDelay: ComputeBatchFreshness(now, lastTravelBatchReceivedUtc),
            QueueDepth: travelQueueDepth,
            DroppedBatches: travelDroppedBatches);
    }

    private IReadOnlyList<LiveImuUiSnapshot> CreateImuSnapshots(DateTimeOffset now)
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
                    SampleDelay: null,
                    QueueDepth: imuQueueDepth,
                    DroppedBatches: imuDroppedBatches));
                continue;
            }

            var sampleOffset = CreateSampleOffset(reading.MonotonicUs);
            snapshots.Add(new LiveImuUiSnapshot(
                Location: location,
                HasData: true,
                Ax: reading.Record.Ax,
                Ay: reading.Record.Ay,
                Az: reading.Record.Az,
                Gx: reading.Record.Gx,
                Gy: reading.Record.Gy,
                Gz: reading.Record.Gz,
                SampleOffset: sampleOffset,
                SampleDelay: ComputeBatchFreshness(now, lastImuBatchReceivedUtc),
                QueueDepth: imuQueueDepth,
                DroppedBatches: imuDroppedBatches));
        }

        return snapshots;
    }

    private LiveGpsUiSnapshot CreateGpsSnapshot(LiveSessionContractSnapshot session)
    {
        var isActive = session.AcceptedSensorMask.HasFlag(LiveSensorInstanceMask.Gps);
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

    private IReadOnlyList<LiveTemperatureUiSnapshot> CreateTemperatureSnapshots(DateTimeOffset now)
    {
        if (sessionHeader is null)
        {
            return [];
        }

        var snapshots = new List<LiveTemperatureUiSnapshot>();
        foreach (var location in sessionHeader.GetActiveTemperatureLocations())
        {
            if (!latestTemperatureReadings.TryGetValue(location, out var reading))
            {
                snapshots.Add(new LiveTemperatureUiSnapshot(location, false, null, null, null));
                continue;
            }

            snapshots.Add(new LiveTemperatureUiSnapshot(
                location,
                true,
                reading.Sample.TemperatureCelsius,
                CreateSampleOffset(reading.MonotonicUs),
                ComputeBatchFreshness(now, lastTemperatureBatchReceivedUtc)));
        }
        return snapshots;
    }

    private static TimeSpan? ComputeBatchFreshness(DateTimeOffset now, DateTimeOffset? lastBatchReceivedUtc)
    {
        if (lastBatchReceivedUtc is not { } batchUtc)
        {
            return null;
        }

        return now - batchUtc;
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
    private readonly record struct LiveTemperatureReading(TemperatureSample Sample, ulong MonotonicUs);
}

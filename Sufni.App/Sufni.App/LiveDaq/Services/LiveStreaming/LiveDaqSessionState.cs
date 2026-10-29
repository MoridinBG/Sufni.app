using System;
using System.Collections.Generic;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

// Latest diagnostics readings and session contract, published as coherent snapshots.
public sealed class LiveDaqSessionState
{
    private readonly System.Threading.Lock gate = new();
    private readonly Dictionary<LiveImuLocation, ImuRecord> latestImuReadings = [];

    private LiveSessionHeader? sessionHeader;
    private LiveTravelRecord? latestTravel;
    private GpsRecord? latestGps;
    private DateTimeOffset? lastFrameReceivedUtc;
    private DateTimeOffset? lastTravelBatchReceivedUtc;
    private DateTimeOffset? lastImuBatchReceivedUtc;
    private uint travelQueueDepth;
    private uint imuQueueDepth;
    private uint gpsQueueDepth;

    public void Reset()
    {
        lock (gate)
        {
            sessionHeader = null;
            latestTravel = null;
            latestGps = null;
            lastFrameReceivedUtc = null;
            lastTravelBatchReceivedUtc = null;
            lastImuBatchReceivedUtc = null;
            travelQueueDepth = 0;
            imuQueueDepth = 0;
            gpsQueueDepth = 0;
            latestImuReadings.Clear();
        }
    }

    public void ApplySharedSessionState(LiveSessionHeader? nextSessionHeader)
    {
        lock (gate)
        {
            sessionHeader = nextSessionHeader;
        }
    }

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

                case LiveSessionStatsFrame sessionStatsFrame:
                    ApplySessionStats(sessionStatsFrame.Payload);
                    break;
            }
        }
    }

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
                Gps: CreateGpsSnapshot());
        }
    }

    private void ApplyTravelBatch(LiveTravelBatchFrame frame)
    {
        if (frame.Records.Count == 0)
        {
            return;
        }

        latestTravel = frame.Records[^1];
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
        for (var index = 0; index < activeLocations.Count; index++)
        {
            latestImuReadings[activeLocations[index]] = frame.Records[lastTickOffset + index];
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

    private void ApplySessionStats(LiveSessionStats stats)
    {
        travelQueueDepth = stats.TravelQueueDepth;
        imuQueueDepth = stats.ImuQueueDepth;
        gpsQueueDepth = stats.GpsQueueDepth;
    }

    private LiveSessionContractSnapshot CreateSessionSnapshot()
    {
        if (sessionHeader is null)
        {
            return LiveSessionContractSnapshot.Empty;
        }

        return new LiveSessionContractSnapshot(
            SessionId: sessionHeader.SessionId,
            RequestedSensorMask: sessionHeader.RequestedSensorMask,
            AcceptedSensorMask: sessionHeader.AcceptedSensorMask,
            AcceptedTravelHz: sessionHeader.AcceptedTravelHz,
            AcceptedImuHz: sessionHeader.AcceptedImuHz,
            AcceptedGpsFixHz: sessionHeader.AcceptedGpsFixHz,
            SessionStartUtc: sessionHeader.SessionStartUtc,
            ActiveImuLocations: sessionHeader.GetActiveImuLocations(),
            AcceptedTravelRateMhz: sessionHeader.AcceptedTravelRateMhz,
            AcceptedImuRateMhz: sessionHeader.AcceptedImuRateMhz,
            AcceptedGpsRateMhz: sessionHeader.AcceptedGpsRateMhz);
    }

    private LiveTravelUiSnapshot CreateTravelSnapshot(LiveSessionContractSnapshot session, DateTimeOffset now)
    {
        var acceptedTravelMask = session.AcceptedSensorMask & LiveSensorInstanceMask.Travel;
        var frontIsActive = acceptedTravelMask.HasFlag(LiveSensorInstanceMask.ForkTravel);
        var rearIsActive = acceptedTravelMask.HasFlag(LiveSensorInstanceMask.ShockTravel);
        if (latestTravel is not LiveTravelRecord travel)
        {
            return LiveTravelUiSnapshot.Empty with
            {
                FrontIsActive = frontIsActive,
                RearIsActive = rearIsActive,
                QueueDepth = travelQueueDepth,
            };
        }

        var frontMeasurement = frontIsActive ? travel.ForkAngle : (ushort?)null;
        var rearMeasurement = rearIsActive ? travel.ShockAngle : (ushort?)null;
        return new LiveTravelUiSnapshot(
            FrontIsActive: frontIsActive,
            RearIsActive: rearIsActive,
            HasData: frontMeasurement is not null || rearMeasurement is not null,
            FrontMeasurement: frontMeasurement,
            RearMeasurement: rearMeasurement,
            SampleDelay: ComputeBatchFreshness(now, lastTravelBatchReceivedUtc),
            QueueDepth: travelQueueDepth);
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
                    SampleDelay: null,
                    QueueDepth: imuQueueDepth));
                continue;
            }

            snapshots.Add(new LiveImuUiSnapshot(
                Location: location,
                HasData: true,
                Ax: reading.Ax,
                Ay: reading.Ay,
                Az: reading.Az,
                Gx: reading.Gx,
                Gy: reading.Gy,
                Gz: reading.Gz,
                SampleDelay: ComputeBatchFreshness(now, lastImuBatchReceivedUtc),
                QueueDepth: imuQueueDepth));
        }

        return snapshots;
    }

    private LiveGpsUiSnapshot CreateGpsSnapshot()
    {
        if (latestGps is null)
        {
            return LiveGpsUiSnapshot.Empty with
            {
                PreviewState = GpsPreviewState.NoFix,
                QueueDepth = gpsQueueDepth,
            };
        }

        return new LiveGpsUiSnapshot(
            HasData: true,
            PreviewState: GpsPreviewState.FromRecord(latestGps),
            FixTimestampUtc: latestGps.Timestamp.ToUniversalTime(),
            Latitude: latestGps.Latitude,
            Longitude: latestGps.Longitude,
            QueueDepth: gpsQueueDepth);
    }

    private static TimeSpan? ComputeBatchFreshness(DateTimeOffset now, DateTimeOffset? lastBatchReceivedUtc)
    {
        if (lastBatchReceivedUtc is not { } batchUtc)
        {
            return null;
        }

        return now - batchUtc;
    }
}

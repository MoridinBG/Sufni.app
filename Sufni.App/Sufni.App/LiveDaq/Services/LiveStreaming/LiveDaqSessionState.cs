using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

// Latest diagnostics readings and session contract, published as coherent snapshots.
public sealed class LiveDaqSessionState(TimeProvider timeProvider)
{
    private sealed record DiagnosticsState(
        LiveSessionHeader? SessionHeader = null,
        LiveTravelRecord? Travel = null,
        GpsRecord? Gps = null,
        DateTimeOffset? LastFrameReceivedUtc = null,
        DateTimeOffset? LastTravelBatchReceivedUtc = null,
        DateTimeOffset? LastImuBatchReceivedUtc = null,
        uint TravelQueueDepth = 0,
        uint ImuQueueDepth = 0,
        uint GpsQueueDepth = 0)
    {
        public ImmutableDictionary<LiveImuLocation, ImuRecord> ImuReadings { get; init; } =
            ImmutableDictionary<LiveImuLocation, ImuRecord>.Empty;
    }

    private readonly System.Threading.Lock gate = new();
    private DiagnosticsState state = new();

    public void Reset()
    {
        lock (gate) state = new();
    }

    public void ApplySharedSessionState(LiveSessionHeader? nextSessionHeader)
    {
        lock (gate) state = state with { SessionHeader = nextSessionHeader };
    }

    public void ApplyFrame(LiveProtocolFrame frame)
    {
        lock (gate) state = Reduce(state, frame, timeProvider.GetUtcNow());
    }

    private static DiagnosticsState Reduce(DiagnosticsState current, LiveProtocolFrame frame, DateTimeOffset receivedUtc)
    {
        var next = current with { LastFrameReceivedUtc = receivedUtc };
        return frame switch
        {
            LiveTravelBatchFrame travel when travel.Records.Count > 0 => next with
            {
                Travel = travel.Records[^1], LastTravelBatchReceivedUtc = receivedUtc
            },
            LiveImuBatchFrame imu => ApplyImuBatch(next, imu),
            LiveGpsBatchFrame gps when gps.Records.Count > 0 => next with { Gps = gps.Records[^1] },
            LiveSessionStatsFrame stats => next with
            {
                TravelQueueDepth = stats.Payload.TravelQueueDepth,
                ImuQueueDepth = stats.Payload.ImuQueueDepth,
                GpsQueueDepth = stats.Payload.GpsQueueDepth
            },
            _ => next
        };
    }

    public LiveDaqUiSnapshot CreateSnapshot(LiveConnectionState connectionState)
    {
        DiagnosticsState current;
        lock (gate) current = state;
        var now = timeProvider.GetUtcNow();
        var session = CreateSessionSnapshot(current);
        return new LiveDaqUiSnapshot(
            ConnectionState: connectionState,
            LastFrameReceivedUtc: current.LastFrameReceivedUtc,
            Session: session,
            Travel: CreateTravelSnapshot(current, now),
            Imus: CreateImuSnapshots(current, now),
            Gps: CreateGpsSnapshot(current));
    }

    private static DiagnosticsState ApplyImuBatch(DiagnosticsState current, LiveImuBatchFrame frame)
    {
        if (frame.Records.Count == 0 || current.SessionHeader is null) return current;
        var locations = current.SessionHeader.GetActiveImuLocations();
        if (locations.Count == 0 || frame.Records.Count < locations.Count) return current;
        var readings = current.ImuReadings.ToBuilder();
        var lastTickOffset = frame.Records.Count - locations.Count;
        for (var index = 0; index < locations.Count; index++)
            readings[locations[index]] = frame.Records[lastTickOffset + index];
        return current with
        {
            ImuReadings = readings.ToImmutable(), LastImuBatchReceivedUtc = current.LastFrameReceivedUtc
        };
    }

    private static LiveSessionContractSnapshot CreateSessionSnapshot(DiagnosticsState current)
    {
        var sessionHeader = current.SessionHeader;
        if (sessionHeader is null)
        {
            return LiveSessionContractSnapshot.Empty;
        }

        return new LiveSessionContractSnapshot(
            SessionId: sessionHeader.SessionId,
            SessionStartUtc: sessionHeader.SessionStartUtc,
            AcceptedTravelRateMhz: sessionHeader.AcceptedTravelRateMhz,
            AcceptedImuRateMhz: sessionHeader.AcceptedImuRateMhz,
            AcceptedGpsRateMhz: sessionHeader.AcceptedGpsRateMhz);
    }

    private static LiveTravelUiSnapshot CreateTravelSnapshot(DiagnosticsState current, DateTimeOffset now)
    {
        var acceptedTravelMask = (current.SessionHeader?.AcceptedSensorMask ?? LiveSensorInstanceMask.None) & LiveSensorInstanceMask.Travel;
        var frontIsActive = acceptedTravelMask.HasFlag(LiveSensorInstanceMask.ForkTravel);
        var rearIsActive = acceptedTravelMask.HasFlag(LiveSensorInstanceMask.ShockTravel);
        if (current.Travel is not LiveTravelRecord travel)
        {
            return LiveTravelUiSnapshot.Empty with
            {
                FrontIsActive = frontIsActive,
                RearIsActive = rearIsActive,
                QueueDepth = current.TravelQueueDepth,
            };
        }

        var frontMeasurement = frontIsActive ? travel.ForkAngle : (ushort?)null;
        var rearMeasurement = rearIsActive ? travel.ShockAngle : (ushort?)null;
        return new LiveTravelUiSnapshot(
            FrontIsActive: frontIsActive,
            RearIsActive: rearIsActive,
            FrontMeasurement: frontMeasurement,
            RearMeasurement: rearMeasurement,
            SampleDelay: ComputeBatchFreshness(now, current.LastTravelBatchReceivedUtc),
            QueueDepth: current.TravelQueueDepth);
    }

    private static IReadOnlyList<LiveImuUiSnapshot> CreateImuSnapshots(DiagnosticsState current, DateTimeOffset now)
    {
        var sessionHeader = current.SessionHeader;
        if (sessionHeader is null)
        {
            return [];
        }

        var snapshots = new List<LiveImuUiSnapshot>();
        foreach (var location in sessionHeader.GetActiveImuLocations())
        {
            if (!current.ImuReadings.TryGetValue(location, out var reading))
            {
                snapshots.Add(new LiveImuUiSnapshot(
                    Location: location,
                    Ax: null,
                    Ay: null,
                    Az: null,
                    Gx: null,
                    Gy: null,
                    Gz: null,
                    SampleDelay: null,
                    QueueDepth: current.ImuQueueDepth));
                continue;
            }

            snapshots.Add(new LiveImuUiSnapshot(
                Location: location,
                Ax: reading.Ax,
                Ay: reading.Ay,
                Az: reading.Az,
                Gx: reading.Gx,
                Gy: reading.Gy,
                Gz: reading.Gz,
                SampleDelay: ComputeBatchFreshness(now, current.LastImuBatchReceivedUtc),
                QueueDepth: current.ImuQueueDepth));
        }

        return snapshots;
    }

    private static LiveGpsUiSnapshot CreateGpsSnapshot(DiagnosticsState current)
    {
        var latestGps = current.Gps;
        if (latestGps is null)
        {
            return LiveGpsUiSnapshot.Empty with
            {
                PreviewState = GpsPreviewState.NoFix,
                QueueDepth = current.GpsQueueDepth,
            };
        }

        return new LiveGpsUiSnapshot(
            PreviewState: GpsPreviewState.FromRecord(latestGps),
            FixTimestampUtc: latestGps.Timestamp.ToUniversalTime(),
            Latitude: latestGps.Latitude,
            Longitude: latestGps.Longitude,
            QueueDepth: current.GpsQueueDepth);
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

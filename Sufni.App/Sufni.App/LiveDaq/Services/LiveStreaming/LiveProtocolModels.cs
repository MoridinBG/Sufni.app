using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

[Flags]
public enum LiveStreamMask : uint
{
    None = 0,
    Travel = 1u << 1,
    Imu = 1u << 2,
    Temperature = 1u << 3,
    Gps = 1u << 4,
    Battery = 1u << 5,
    Marker = 1u << 6,
}

[Flags]
public enum LiveSensorInstanceMask : uint
{
    None = 0,
    ForkTravel = 0x00000001,
    ShockTravel = 0x00000002,
    FrameImu = 0x00000004,
    ForkImu = 0x00000008,
    RearImu = 0x00000010,
    Gps = 0x00000020,
    Battery = 0x00000040,
    Travel = ForkTravel | ShockTravel,
    Imu = FrameImu | ForkImu | RearImu,
    All = Travel | Imu | Gps | Battery,
}

[Flags]
public enum LiveSessionFlags : uint
{
    None = 0,
    CalibratedOnly = 0x01,
    MutuallyExclusiveWithRecording = 0x02,
}

[Flags]
public enum LiveImuLocationMask : uint
{
    None = 0,
    Frame = 0x01,
    Fork = 0x02,
    Rear = 0x04,
}

public enum LiveImuLocation
{
    Frame = 0,
    Fork = 1,
    Rear = 2,
}

public enum LiveStartErrorCode : int
{
    Ok = 0,
    InvalidRequest = -1,
    Busy = -2,
    NoSensorsStarted = -5,
}

public readonly record struct LiveFrameMetadata(uint Sequence);

public readonly record struct LiveStartAdmissionReason(
    byte Reason,
    LiveStreamMask Stream,
    LiveSensorInstanceMask Sources,
    byte TargetKind = 0,
    uint TargetMask = 0,
    byte StreamKind = 0);

// Client-to-DAQ request. Rates are in millihertz so live v2, live v3, and SST
// descriptors share one internal unit. Zero means no preference.
public readonly record struct LiveStartRequest(
    LiveSensorInstanceMask RequestedSensorMask,
    uint TravelRateMhz,
    uint ImuRateMhz,
    uint GpsRateMhz,
    LiveStreamMask RequestedStreamMask = LiveStreamMask.None,
    uint TemperatureRateMhz = 0,
    uint? TravelBatchDurationMs = null,
    uint? ImuBatchDurationMs = null,
    bool RequestGpsDiagnostics = false,
    bool Priority = false,
    bool NoGpsHeaderWait = false);

public readonly record struct LiveStartAck(
    LiveStartErrorCode Result,
    uint SessionId,
    LiveStreamMask SelectedStreamMask);

public sealed record LiveImuCalibrationScales(
    float FrameAccelLsbPerG,
    float ForkAccelLsbPerG,
    float RearAccelLsbPerG,
    float FrameGyroLsbPerDps,
    float ForkGyroLsbPerDps,
    float RearGyroLsbPerDps)
{
    public float GetAccelScale(LiveImuLocation location) => location switch
    {
        LiveImuLocation.Frame => FrameAccelLsbPerG,
        LiveImuLocation.Fork => ForkAccelLsbPerG,
        LiveImuLocation.Rear => RearAccelLsbPerG,
        _ => 0,
    };

    public float GetGyroScale(LiveImuLocation location) => location switch
    {
        LiveImuLocation.Frame => FrameGyroLsbPerDps,
        LiveImuLocation.Fork => ForkGyroLsbPerDps,
        LiveImuLocation.Rear => RearGyroLsbPerDps,
        _ => 0,
    };
}


// Sent by the DAQ after START_LIVE_ACK to describe the accepted session parameters.
// Contains the actual sampling rates the firmware chose (which may differ from requested),
// timing bases for monotonic-to-wall-clock conversion, active IMU sensor topology,
// and per-location calibration scales.
public sealed record LiveSessionHeader(
    uint SessionId,
    uint AcceptedTravelRateMhz,      // actual travel sampling rate in millihertz
    uint AcceptedImuRateMhz,         // actual IMU sampling rate in millihertz
    uint AcceptedGpsRateMhz,         // actual GPS fix rate in millihertz
    DateTimeOffset SessionStartUtc,  // wall-clock session start
    ulong SessionStartMonotonicUs,   // firmware monotonic clock at session start
    LiveImuLocationMask ActiveImuMask,
    LiveImuCalibrationScales ImuCalibrationScales,
    LiveSessionFlags Flags,
    LiveSensorInstanceMask RequestedSensorMask,
    LiveSensorInstanceMask AcceptedSensorMask,
    LiveProtocolVersion ProtocolVersion = LiveProtocolVersion.V2)
{
    public byte BoardId { get; init; }
    public LiveStreamMask RequestedStreamMask { get; init; }
    public LiveStreamMask AcceptedStreamMask { get; init; }
    public uint AcceptedTemperatureRateMhz { get; init; }
    public IReadOnlyList<LiveStartAdmissionReason> AdmissionOmissions { get; init; } = [];
    public IReadOnlyList<SstV5StreamDescriptor> StreamDescriptors { get; init; } = [];

    public double AcceptedTravelHzDouble => AcceptedTravelRateMhz / 1000.0;
    public double AcceptedImuHzDouble => AcceptedImuRateMhz / 1000.0;
    public double AcceptedGpsHzDouble => AcceptedGpsRateMhz / 1000.0;

    public uint AcceptedTravelHz => LiveProtocolHelpers.MillihertzToWholeHertz(AcceptedTravelRateMhz);
    public uint AcceptedImuHz => LiveProtocolHelpers.MillihertzToWholeHertz(AcceptedImuRateMhz);
    public uint AcceptedGpsFixHz => LiveProtocolHelpers.MillihertzToWholeHertz(AcceptedGpsRateMhz);
    public uint AcceptedTemperatureHz => LiveProtocolHelpers.MillihertzToWholeHertz(AcceptedTemperatureRateMhz);

    public uint TravelPeriodUs => AcceptedTravelRateMhz == 0 ? 0 : checked((uint)Math.Round(1_000_000_000.0 / AcceptedTravelRateMhz, MidpointRounding.AwayFromZero));
    public uint ImuPeriodUs => AcceptedImuRateMhz == 0 ? 0 : checked((uint)Math.Round(1_000_000_000.0 / AcceptedImuRateMhz, MidpointRounding.AwayFromZero));
    public uint GpsFixIntervalMs => AcceptedGpsRateMhz == 0 ? 0 : checked((uint)Math.Round(1_000_000.0 / AcceptedGpsRateMhz, MidpointRounding.AwayFromZero));
    public LiveSensorInstanceMask MissingSensorMask => RequestedSensorMask & ~AcceptedSensorMask;
    public IReadOnlyList<LiveImuLocation> GetActiveImuLocations() => LiveProtocolHelpers.GetActiveImuLocations(ActiveImuMask);
    public IReadOnlyList<LiveImuLocation> GetActiveTemperatureLocations() =>
        StreamDescriptors
            .Where(stream => stream.StreamKind == SstV5ProtocolConstants.StreamTemperature)
            .SelectMany(stream => stream.Sources)
            .Select(source => (LiveImuLocation)SstV5ProtocolConstants.GetImuLocationId(source.SourceBitMask))
            .ToArray();
}

public readonly record struct LiveStopAck(uint SessionId);

public readonly record struct LiveIdentifyAck(byte[] BoardSerial);

public readonly record struct LiveError(LiveStartErrorCode ErrorCode)
{
    public int RawCode => (int)ErrorCode;
}

// Common header for every DATA_BATCH / IMU_BATCH / GPS_BATCH frame. Identifies where this
// batch sits in its stream's sequence and the firmware monotonic timestamp of the first sample.
// Stream type is implicit in the frame type; payload size is derived from sample_count.
public readonly record struct LiveBatchHeader(
    uint SessionId,
    LiveStreamMask Stream,
    uint StreamSequence,       // per-stream monotonic counter (detects gaps)
    ulong FirstIndex,          // absolute sample index of the first record in this batch
    ulong FirstMonotonicDeltaUs,
    ulong FirstMonotonicUs,    // firmware monotonic timestamp of the first sample
    uint SampleCount,          // number of records in the batch payload
    LiveSensorInstanceMask ValidityMask)
{
    public LiveBatchHeader(
        uint SessionId,
        uint StreamSequence,
        ulong FirstIndex,
        ulong FirstMonotonicUs,
        uint SampleCount)
        : this(
            SessionId,
            LiveStreamMask.None,
            StreamSequence,
            FirstIndex,
            FirstMonotonicDeltaUs: 0,
            FirstMonotonicUs,
            SampleCount,
            LiveSensorInstanceMask.All)
    {
    }
}

public readonly record struct LiveTravelRecord(ushort ForkAngle, ushort ShockAngle);

public readonly record struct LiveBatteryRecord(
    ulong SampleIndex,
    ulong MonotonicDeltaUs,
    ushort Millivolts,
    ushort Flags);

public readonly record struct LiveMarkerRecord(
    ulong SampleIndex,
    ulong MonotonicDeltaUs,
    byte MarkerType);

public readonly record struct LiveTemperatureRecord(
    ulong SampleIndex,
    ulong MonotonicDeltaUs,
    LiveSensorInstanceMask Source,
    TemperatureSample Sample);

public sealed record LiveStreamDescriptor(
    LiveStreamMask Stream,
    uint AcceptedRateMhz,
    uint BatchDurationMs,
    uint CompactPayloadRecordBytes,
    LiveSensorInstanceMask AcceptedSourceMask,
    IReadOnlyList<LiveSourceDescriptor> Sources);

public sealed record LiveSourceDescriptor(
    LiveStreamMask Stream,
    LiveSensorInstanceMask Source,
    ushort PayloadOffsetBytes,
    ushort PayloadRecordBytes,
    byte PayloadEncodingId,
    byte LocationId);

public sealed record LiveStreamStatus(
    LiveStreamMask Stream,
    byte ProducerState,
    byte ProducerFailureReason,
    ushort SinkBacklogBatches,
    ulong ProducerMissedCount,
    ulong ProducerMissingTimeUs,
    ulong SinkMissedCount,
    ulong SinkMissingTimeUs);

public sealed record LiveStopResult(
    uint SessionId,
    bool Accepted,
    byte Reason);

public sealed record LiveSessionResult(
    uint SessionId,
    SstFinalStatus FinalStatus);

// Periodic health snapshot sent by the DAQ during a live session. Contains the current queue
// depths and cumulative drop counts for each stream.
public sealed record LiveSessionStats(
    uint SessionId,
    uint TravelQueueDepth,
    uint ImuQueueDepth,
    uint GpsQueueDepth,
    uint TravelDroppedBatches,
    uint ImuDroppedBatches,
    uint GpsDroppedBatches);

public abstract record LiveProtocolFrame(LiveFrameMetadata Header)
{
    public uint Sequence => Header.Sequence;
}

public sealed record LiveStartRequestFrame(LiveFrameMetadata Header, LiveStartRequest Payload) : LiveProtocolFrame(Header);
public sealed record LiveStopRequestFrame(LiveFrameMetadata Header) : LiveProtocolFrame(Header);
public sealed record LivePingFrame(LiveFrameMetadata Header, uint? Nonce = null) : LiveProtocolFrame(Header)
{
    public byte SessionId { get; init; }
}
public sealed record LiveIdentifyRequestFrame(LiveFrameMetadata Header) : LiveProtocolFrame(Header);
public sealed record LiveStartAckFrame(LiveFrameMetadata Header, LiveStartAck Payload) : LiveProtocolFrame(Header);
public sealed record LiveSessionHeaderFrame(LiveFrameMetadata Header, LiveSessionHeader Payload) : LiveProtocolFrame(Header);
public sealed record LiveStopAckFrame(LiveFrameMetadata Header, LiveStopAck Payload) : LiveProtocolFrame(Header);
public sealed record LiveStopResultFrame(LiveFrameMetadata Header, LiveStopResult Payload) : LiveProtocolFrame(Header);
public sealed record LiveErrorFrame(LiveFrameMetadata Header, LiveError Payload) : LiveProtocolFrame(Header);
public sealed record LivePongFrame(LiveFrameMetadata Header, uint? Nonce = null) : LiveProtocolFrame(Header)
{
    public byte SessionId { get; init; }
}
public sealed record LiveIdentifyAckFrame(LiveFrameMetadata Header, LiveIdentifyAck Payload) : LiveProtocolFrame(Header);
public sealed record LiveTravelBatchFrame(LiveFrameMetadata Header, LiveBatchHeader Batch, IReadOnlyList<LiveTravelRecord> Records) : LiveProtocolFrame(Header);
public sealed record LiveImuBatchFrame(LiveFrameMetadata Header, LiveBatchHeader Batch, IReadOnlyList<ImuRecord> Records) : LiveProtocolFrame(Header);
public sealed record LiveTemperatureBatchFrame(LiveFrameMetadata Header, LiveBatchHeader Batch, IReadOnlyList<LiveTemperatureRecord> Records) : LiveProtocolFrame(Header);
public sealed record LiveGpsBatchFrame(LiveFrameMetadata Header, LiveBatchHeader Batch, IReadOnlyList<GpsRecord> Records) : LiveProtocolFrame(Header);
public sealed record LiveBatteryBatchFrame(LiveFrameMetadata Header, LiveBatchHeader Batch, IReadOnlyList<LiveBatteryRecord> Records) : LiveProtocolFrame(Header);
public sealed record LiveMarkerBatchFrame(LiveFrameMetadata Header, LiveBatchHeader Batch, IReadOnlyList<LiveMarkerRecord> Records) : LiveProtocolFrame(Header);
public sealed record LiveStatusFrame(LiveFrameMetadata Header, IReadOnlyList<LiveStreamStatus> Streams) : LiveProtocolFrame(Header);
public sealed record LiveSessionResultFrame(LiveFrameMetadata Header, LiveSessionResult Payload) : LiveProtocolFrame(Header);
public sealed record LiveSessionStatsFrame(LiveFrameMetadata Header, LiveSessionStats Payload) : LiveProtocolFrame(Header);

public abstract record LivePreviewStartResult
{
    public sealed record Started(LiveSessionHeader Header) : LivePreviewStartResult;
    public sealed record Rejected(LiveStartErrorCode ErrorCode, string UserMessage) : LivePreviewStartResult
    {
        public IReadOnlyList<LiveStartAdmissionReason> AdmissionReasons { get; init; } = [];
    }

    public sealed record Failed(string ErrorMessage) : LivePreviewStartResult;
}

public abstract record LiveDaqClientEvent
{
    public sealed record FrameReceived(LiveProtocolFrame Frame) : LiveDaqClientEvent;
    public sealed record DropCountersChanged(LiveDaqClientDropCounters Counters) : LiveDaqClientEvent;
    public sealed record Disconnected(string? ErrorMessage) : LiveDaqClientEvent;
    public sealed record Faulted(string ErrorMessage) : LiveDaqClientEvent;
}

public static class LiveProtocolHelpers
{
    public const uint MillihertzPerHertz = 1000;

    public static uint HertzToMillihertz(uint hertz) => checked(hertz * MillihertzPerHertz);

    public static uint MillihertzToWholeHertz(uint rateMhz) =>
        checked((uint)Math.Round(rateMhz / (double)MillihertzPerHertz, MidpointRounding.AwayFromZero));

    public static uint MillihertzToV2WireHertz(uint rateMhz) =>
        rateMhz < MillihertzPerHertz ? 0 : MillihertzToWholeHertz(rateMhz);

    public static string FormatMillihertzAsHertz(uint rateMhz) =>
        (rateMhz / (double)MillihertzPerHertz).ToString("0.##", CultureInfo.InvariantCulture);

    public static string FormatRateText(string label, uint? rateMhz) =>
        rateMhz is { } value
            ? $"{label}: {FormatMillihertzAsHertz(value)} Hz"
            : $"{label}: -";

    public static LiveSessionStats CreateSessionStatsFromStatus(IReadOnlyList<LiveStreamStatus> statuses) => new(
        SessionId: 0,
        TravelQueueDepth: 0,
        ImuQueueDepth: 0,
        GpsQueueDepth: 0,
        TravelDroppedBatches: GetDroppedStatusCount(statuses, LiveStreamMask.Travel),
        ImuDroppedBatches: GetDroppedStatusCount(statuses, LiveStreamMask.Imu),
        GpsDroppedBatches: GetDroppedStatusCount(statuses, LiveStreamMask.Gps));

    public static IReadOnlyList<LiveImuLocation> GetActiveImuLocations(LiveImuLocationMask mask)
    {
        var locations = new List<LiveImuLocation>(3);
        if (mask.HasFlag(LiveImuLocationMask.Frame)) locations.Add(LiveImuLocation.Frame);
        if (mask.HasFlag(LiveImuLocationMask.Fork)) locations.Add(LiveImuLocation.Fork);
        if (mask.HasFlag(LiveImuLocationMask.Rear)) locations.Add(LiveImuLocation.Rear);
        return locations;
    }

    extension(LiveImuLocation location)
    {
        public string DisplayName => location switch
        {
            LiveImuLocation.Frame => "Frame",
            LiveImuLocation.Fork => "Fork",
            LiveImuLocation.Rear => "Rear",
            _ => location.ToString(),
        };
    }

    extension(LiveSensorInstanceMask sensor)
    {
        public string DisplayName => sensor switch
        {
            LiveSensorInstanceMask.ForkTravel => "fork travel",
            LiveSensorInstanceMask.ShockTravel => "shock travel",
            LiveSensorInstanceMask.FrameImu => "frame IMU",
            LiveSensorInstanceMask.ForkImu => "fork IMU",
            LiveSensorInstanceMask.RearImu => "rear IMU",
            LiveSensorInstanceMask.Gps => "GPS",
            LiveSensorInstanceMask.Battery => "battery",
            _ => sensor.ToString(),
        };
    }

    public static IReadOnlyList<string> GetSensorInstanceDisplayNames(LiveSensorInstanceMask mask)
    {
        var names = new List<string>(6);
        if (mask.HasFlag(LiveSensorInstanceMask.ForkTravel)) names.Add(LiveSensorInstanceMask.ForkTravel.DisplayName);
        if (mask.HasFlag(LiveSensorInstanceMask.ShockTravel)) names.Add(LiveSensorInstanceMask.ShockTravel.DisplayName);
        if (mask.HasFlag(LiveSensorInstanceMask.FrameImu)) names.Add(LiveSensorInstanceMask.FrameImu.DisplayName);
        if (mask.HasFlag(LiveSensorInstanceMask.ForkImu)) names.Add(LiveSensorInstanceMask.ForkImu.DisplayName);
        if (mask.HasFlag(LiveSensorInstanceMask.RearImu)) names.Add(LiveSensorInstanceMask.RearImu.DisplayName);
        if (mask.HasFlag(LiveSensorInstanceMask.Gps)) names.Add(LiveSensorInstanceMask.Gps.DisplayName);
        if (mask.HasFlag(LiveSensorInstanceMask.Battery)) names.Add(LiveSensorInstanceMask.Battery.DisplayName);
        return names;
    }

    public static LiveStreamMask ToStreamMask(LiveV2StreamMask mask)
    {
        var streamMask = LiveStreamMask.None;
        if ((mask & LiveV2StreamMask.Travel) != LiveV2StreamMask.None) streamMask |= LiveStreamMask.Travel;
        if ((mask & LiveV2StreamMask.Imu) != LiveV2StreamMask.None) streamMask |= LiveStreamMask.Imu;
        if ((mask & LiveV2StreamMask.Gps) != LiveV2StreamMask.None) streamMask |= LiveStreamMask.Gps;
        return streamMask;
    }

    public static LiveV2StreamMask ToV2StreamMask(LiveStreamMask mask)
    {
        var streamMask = LiveV2StreamMask.None;
        if ((mask & LiveStreamMask.Travel) != LiveStreamMask.None) streamMask |= LiveV2StreamMask.Travel;
        if ((mask & LiveStreamMask.Imu) != LiveStreamMask.None) streamMask |= LiveV2StreamMask.Imu;
        if ((mask & LiveStreamMask.Gps) != LiveStreamMask.None) streamMask |= LiveV2StreamMask.Gps;
        return streamMask;
    }

    private static uint GetDroppedStatusCount(IReadOnlyList<LiveStreamStatus> statuses, LiveStreamMask stream)
    {
        foreach (var status in statuses)
        {
            if (status.Stream == stream)
            {
                return checked((uint)Math.Min((ulong)uint.MaxValue, status.ProducerMissedCount + status.SinkMissedCount));
            }
        }

        return 0;
    }

    extension(LiveSensorInstanceMask mask)
    {
        public LiveStreamMask StreamMask
        {
            get
            {
                var streamMask = LiveStreamMask.None;
                if ((mask & LiveSensorInstanceMask.Travel) != LiveSensorInstanceMask.None) streamMask |= LiveStreamMask.Travel;
                if ((mask & LiveSensorInstanceMask.Imu) != LiveSensorInstanceMask.None) streamMask |= LiveStreamMask.Imu;
                if ((mask & LiveSensorInstanceMask.Gps) != LiveSensorInstanceMask.None) streamMask |= LiveStreamMask.Gps;
                if ((mask & LiveSensorInstanceMask.Battery) != LiveSensorInstanceMask.None) streamMask |= LiveStreamMask.Battery;
                return streamMask;
            }
        }
    }

    extension(LiveStartErrorCode errorCode)
    {
        public string UserMessage => errorCode switch
        {
            LiveStartErrorCode.Ok => "Live preview started.",
            LiveStartErrorCode.InvalidRequest => "Live preview request was invalid.",
            LiveStartErrorCode.Busy => "Live preview is busy. Recording or another live session may already be active.",
            LiveStartErrorCode.NoSensorsStarted => "None of the requested sensors could start.",
            _ => $"The device rejected live preview with error code {(int)errorCode}.",
        };
    }
}

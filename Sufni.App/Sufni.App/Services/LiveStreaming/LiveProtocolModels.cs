using System;
using System.Collections.Generic;
using Sufni.Telemetry;

namespace Sufni.App.Services.LiveStreaming;

public static class LiveProtocolConstants
{
    public const uint Magic = 0x4556494C;
    public const ushort Version = 1;
    public const int DefaultPort = 1557;

    public const int FrameHeaderSize = 16;
    // Hard cap on frame payload length. Prevents a bogus on-wire length field
    // from driving the receive buffer toward OOM. 4 MiB comfortably exceeds
    // any realistic batch (~250 KB at peak).
    public const int MaxPayloadLength = 4 * 1024 * 1024;
    public const int StartRequestPayloadSize = 16;
    public const int StartAckPayloadSize = 12;
    public const int SessionHeaderPayloadSize = 64;
    public const int StopAckPayloadSize = 4;
    public const int ErrorPayloadSize = 4;
    public const int BatchHeaderSize = 28;
    public const int TravelRecordSize = 4;
    public const int ImuRecordSize = 12;
    public const int GpsRecordSize = 46;
    public const int SessionStatsPayloadSize = 28;
    public const int IdentifyAckPayloadSize = 8;
}

public enum LiveFrameType : ushort
{
    StartLive = 1,
    StopLive = 2,
    Ping = 3,
    Identify = 4,
    StartLiveAck = 16,
    StopLiveAck = 17,
    Error = 18,
    Pong = 19,
    IdentifyAck = 21,
    SessionHeader = 20,
    TravelBatch = 32,
    ImuBatch = 33,
    GpsBatch = 34,
    SessionStats = 48,
}

[Flags]
public enum LiveSensorMask : uint
{
    None = 0,
    Travel = 0x01,
    Imu = 0x02,
    Gps = 0x04,
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
    Unavailable = -3,
    InternalError = -4,
}

public readonly record struct LiveFrameHeader(
    uint Magic,
    ushort Version,
    LiveFrameType FrameType,
    uint PayloadLength,
    uint Sequence)
{
    public bool IsValidMagic => Magic == LiveProtocolConstants.Magic;
    public bool IsSupportedVersion => Version == LiveProtocolConstants.Version;
    public int TotalFrameLength => checked(LiveProtocolConstants.FrameHeaderSize + (int)PayloadLength);
}

// Client-to-DAQ request. The Hz fields cap each stream's rate — the DAQ may
// accept a lower rate. Zero means no preference (use the device default).
public readonly record struct LiveStartRequest(
    LiveSensorMask SensorMask,
    uint TravelHz,
    uint ImuHz,
    uint GpsFixHz);

public readonly record struct LiveStartAck(
    LiveStartErrorCode Result,
    uint SessionId,
    LiveSensorMask SelectedSensorMask);

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
    uint AcceptedTravelHz,           // actual travel sampling rate
    uint AcceptedImuHz,              // actual IMU sampling rate
    uint AcceptedGpsFixHz,           // actual GPS fix rate
    DateTimeOffset SessionStartUtc,  // wall-clock session start
    ulong SessionStartMonotonicUs,   // firmware monotonic clock at session start
    LiveImuLocationMask ActiveImuMask,
    LiveImuCalibrationScales ImuCalibrationScales,
    LiveSessionFlags Flags)
{
    public uint TravelPeriodUs => AcceptedTravelHz == 0 ? 0 : 1_000_000 / AcceptedTravelHz;
    public uint ImuPeriodUs => AcceptedImuHz == 0 ? 0 : 1_000_000 / AcceptedImuHz;
    public uint GpsFixIntervalMs => AcceptedGpsFixHz == 0 ? 0 : 1_000 / AcceptedGpsFixHz;
    public IReadOnlyList<LiveImuLocation> GetActiveImuLocations() => LiveProtocolHelpers.GetActiveImuLocations(ActiveImuMask);
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
    uint StreamSequence,       // per-stream monotonic counter (detects gaps)
    ulong FirstIndex,          // absolute sample index of the first record in this batch
    ulong FirstMonotonicUs,    // firmware monotonic timestamp of the first sample
    uint SampleCount);         // number of records in the batch payload

public readonly record struct LiveTravelRecord(ushort ForkAngle, ushort ShockAngle);

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

public abstract record LiveProtocolFrame(LiveFrameHeader Header)
{
    public uint Sequence => Header.Sequence;
}

public sealed record LiveStartRequestFrame(LiveFrameHeader Header, LiveStartRequest Payload) : LiveProtocolFrame(Header);
public sealed record LiveStopRequestFrame(LiveFrameHeader Header) : LiveProtocolFrame(Header);
public sealed record LivePingFrame(LiveFrameHeader Header) : LiveProtocolFrame(Header);
public sealed record LiveIdentifyRequestFrame(LiveFrameHeader Header) : LiveProtocolFrame(Header);
public sealed record LiveStartAckFrame(LiveFrameHeader Header, LiveStartAck Payload) : LiveProtocolFrame(Header);
public sealed record LiveSessionHeaderFrame(LiveFrameHeader Header, LiveSessionHeader Payload) : LiveProtocolFrame(Header);
public sealed record LiveStopAckFrame(LiveFrameHeader Header, LiveStopAck Payload) : LiveProtocolFrame(Header);
public sealed record LiveErrorFrame(LiveFrameHeader Header, LiveError Payload) : LiveProtocolFrame(Header);
public sealed record LivePongFrame(LiveFrameHeader Header) : LiveProtocolFrame(Header);
public sealed record LiveIdentifyAckFrame(LiveFrameHeader Header, LiveIdentifyAck Payload) : LiveProtocolFrame(Header);
public sealed record LiveTravelBatchFrame(LiveFrameHeader Header, LiveBatchHeader Batch, IReadOnlyList<LiveTravelRecord> Records) : LiveProtocolFrame(Header);
public sealed record LiveImuBatchFrame(LiveFrameHeader Header, LiveBatchHeader Batch, IReadOnlyList<ImuRecord> Records) : LiveProtocolFrame(Header);
public sealed record LiveGpsBatchFrame(LiveFrameHeader Header, LiveBatchHeader Batch, IReadOnlyList<GpsRecord> Records) : LiveProtocolFrame(Header);
public sealed record LiveSessionStatsFrame(LiveFrameHeader Header, LiveSessionStats Payload) : LiveProtocolFrame(Header);

public abstract record LivePreviewStartResult
{
    public sealed record Started(LiveSessionHeader Header, LiveSensorMask SelectedSensorMask) : LivePreviewStartResult;
    public sealed record Rejected(LiveStartErrorCode ErrorCode, string UserMessage) : LivePreviewStartResult;
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
    public static IReadOnlyList<LiveImuLocation> GetActiveImuLocations(LiveImuLocationMask mask)
    {
        var locations = new List<LiveImuLocation>(3);
        if (mask.HasFlag(LiveImuLocationMask.Frame)) locations.Add(LiveImuLocation.Frame);
        if (mask.HasFlag(LiveImuLocationMask.Fork)) locations.Add(LiveImuLocation.Fork);
        if (mask.HasFlag(LiveImuLocationMask.Rear)) locations.Add(LiveImuLocation.Rear);
        return locations;
    }

    public static string ToDisplayName(this LiveImuLocation location) => location switch
    {
        LiveImuLocation.Frame => "Frame",
        LiveImuLocation.Fork => "Fork",
        LiveImuLocation.Rear => "Rear",
        _ => location.ToString(),
    };

    public static string ToUserMessage(this LiveStartErrorCode errorCode) => errorCode switch
    {
        LiveStartErrorCode.Ok => "Live preview started.",
        LiveStartErrorCode.InvalidRequest => "Live preview request was invalid.",
        LiveStartErrorCode.Busy => "Live preview is busy. Recording or another live session may already be active.",
        LiveStartErrorCode.Unavailable => "Live preview is unavailable on the device right now.",
        LiveStartErrorCode.InternalError => "The device reported an internal error while starting live preview.",
        _ => $"The device rejected live preview with error code {(int)errorCode}.",
    };
}
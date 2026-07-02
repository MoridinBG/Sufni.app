using System;
using System.Collections.Generic;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public static class LiveV3ProtocolConstants
{
    public const int HandshakeSize = 7;
    public const string HandshakeMagic = "LIV3";
    public const int ServerHelloSize = 24;
    public const byte ProtocolMajor = 3;
    public const int FrameHeaderSize = 12;
    public const int MaxPayloadLength = 4096;
    public const int StartRequestHeaderSize = 4;
    public const int StreamRequestRecordSize = 20;
    public const int MaxStartRequestRecordCount = 6;
    public const ushort StartFlagPriority = 0x0001;
    public const ushort StartFlagNoGpsHeaderWait = 0x0002;
    public const ushort StreamRequestFlagRateOverride = 0x0001;
    public const ushort StreamRequestFlagBatchDurationOverride = 0x0002;
    public const int DataHeaderSize = SstV5ProtocolConstants.DataHeaderSize;

    public const int CapabilitiesHeaderSize = 20;
    public const int StreamCapabilityRecordSize = 20;
    public const int StartResultHeaderSize = 8;
    public const int AdmissionReasonRecordSize = 8;
    public const int SessionHeaderFixedSize = 24;
    public const int StopResultPayloadSize = 4;
    public const int StatusHeaderSize = 4;
    public const int StatusRecordSize = 36;
    public const int DeviceStateHeaderSize = 4;
    public const int StreamStateRecordSize = 12;
    public const int SourceStateRecordSize = 8;
    public const int ErrorPayloadSize = 4;
}

public enum LiveV3FrameType : byte
{
    CapabilitiesReq = 1,
    CapabilitiesResp = 2,
    StartReq = 3,
    StartResult = 4,
    StopReq = 5,
    StopResult = 6,
    SessionHeader = 7,
    SessionResult = 8,
    TravelData = 9,
    ImuData = 10,
    TemperatureData = 11,
    GpsData = 12,
    BatteryData = 13,
    MarkerData = 14,
    Status = 15,
    Ping = 16,
    Pong = 17,
    Error = 18,
    DeviceStateReq = 19,
    DeviceStateResp = 20,
}

public readonly record struct LiveV3ServerHello(
    byte ProtoMinor,
    ushort FeatureFlags,
    uint MaxFramePayloadBytes,
    ulong UniqueBoardId,
    Version FirmwareVersion);

public readonly record struct LiveV3FrameHeader(
    LiveV3FrameType FrameType,
    byte Flags,
    byte SessionId,
    byte Reserved,
    uint PayloadLength,
    uint TxSequence)
{
    public int TotalFrameLength => checked(LiveV3ProtocolConstants.FrameHeaderSize + (int)PayloadLength);
}

public sealed record LiveV3StartRequest
{
    public ushort StartFlags { get; init; }
    public IReadOnlyList<LiveV3StreamRequestRecord> StreamRequests { get; init; } = [];
}

public readonly record struct LiveV3StreamRequestRecord(
    byte StreamKind,
    ushort RecordFlags,
    LiveSensorInstanceMask SourceMask,
    uint ExtensionMask,
    uint RateMhz,
    uint BatchDurationMs);

public readonly record struct LiveV3StartResult(
    byte ResultCode,
    byte SessionId,
    LiveStreamMask AcceptedStreamMask,
    IReadOnlyList<LiveStartAdmissionReason> AdmissionReasons)
{
    public bool IsPending => ResultCode == 0;
    public bool IsDenied => ResultCode == 1;
}

public readonly record struct LiveV3StopResult(
    byte ResultCode,
    byte Reason)
{
    public bool Accepted => ResultCode == 0;
}

public readonly record struct LiveV3SessionResult(SstFinalStatus FinalStatus);

public sealed record LiveV3Capabilities(
    uint MaxFramePayloadBytes,
    LiveStreamMask SupportedStreamMask,
    LiveSensorInstanceMask SupportedSourceMask,
    uint SupportedExtensionMask,
    IReadOnlyList<LiveV3StreamCapability> Streams);

public sealed record LiveV3StreamCapability(
    LiveStreamMask Stream,
    LiveSensorInstanceMask SupportedSourceMask,
    uint SupportedExtensionMask,
    uint MinRateMhz,
    uint MaxRateMhz);

public sealed record LiveV3DeviceState(
    IReadOnlyList<LiveV3StreamState> Streams,
    IReadOnlyList<LiveV3SourceState> Sources);

public readonly record struct LiveV3StreamState(
    byte StreamKind,
    uint EffectiveDefaultRateMhz,
    uint DefaultBatchDurationMs);

public readonly record struct LiveV3SourceState(
    byte CalibrationStatus,
    bool Available,
    LiveSensorInstanceMask Source);

public readonly record struct LiveV3AdmissionReason(
    byte TargetKind,
    byte StreamKind,
    byte Reason,
    uint TargetMask);

public readonly record struct LiveV3StreamStatus(
    byte StreamKind,
    byte ProducerState,
    byte ProducerFailureReason,
    ushort SinkBacklogBatches,
    ulong ProducerMissedCount,
    ulong ProducerMissingTimeUs,
    ulong SinkMissedCount,
    ulong SinkMissingTimeUs);

public readonly record struct LiveV3Error(byte Code);

public sealed record LiveV3CapabilitiesRequestFrame(LiveFrameMetadata Header) : LiveProtocolFrame(Header);
public sealed record LiveV3CapabilitiesFrame(LiveFrameMetadata Header, LiveV3Capabilities Payload) : LiveProtocolFrame(Header);
public sealed record LiveV3StartRequestFrame(LiveFrameMetadata Header, LiveV3StartRequest Payload) : LiveProtocolFrame(Header);
public sealed record LiveV3StartResultFrame(LiveFrameMetadata Header, LiveV3StartResult Payload) : LiveProtocolFrame(Header);
public sealed record LiveV3ErrorFrame(LiveFrameMetadata Header, LiveV3Error Payload) : LiveProtocolFrame(Header);
public sealed record LiveV3DeviceStateRequestFrame(LiveFrameMetadata Header) : LiveProtocolFrame(Header);
public sealed record LiveV3DeviceStateFrame(LiveFrameMetadata Header, LiveV3DeviceState Payload) : LiveProtocolFrame(Header);

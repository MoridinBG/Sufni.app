using System;
using Sufni.Telemetry;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

public static class LiveV2ProtocolConstants
{
    public const uint Magic = 0x4556494C;
    public const ushort Version = 2;
    public const int DefaultPort = 1557;

    public const int FrameHeaderSize = 16;
    // Hard cap on frame payload length. Prevents a bogus on-wire length field
    // from driving the receive buffer toward OOM. 4 MiB comfortably exceeds
    // any realistic batch (~250 KB at peak).
    public const int MaxPayloadLength = 4 * 1024 * 1024;
    public const int StartRequestPayloadSize = 16;
    public const int StartAckPayloadSize = 12;
    public const int SessionHeaderPayloadSize = 72;
    public const int StopAckPayloadSize = 4;
    public const int ErrorPayloadSize = 4;
    public const int BatchHeaderSize = 28;
    public const int TravelRecordSize = 4;
    public const int ImuRecordSize = 12;
    public const int GpsRecordSize = GpsBinaryRecordDecoder.RecordSize;
    public const int SessionStatsPayloadSize = 28;
    public const int IdentifyAckPayloadSize = 8;
}

public enum LiveV2FrameType : ushort
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
public enum LiveV2StreamMask : uint
{
    None = 0,
    Travel = 0x01,
    Imu = 0x02,
    Gps = 0x04,
}

public readonly record struct LiveV2FrameHeader(
    uint Magic,
    ushort Version,
    LiveV2FrameType FrameType,
    uint PayloadLength,
    uint Sequence)
{
    public bool IsValidMagic => Magic == LiveV2ProtocolConstants.Magic;
    public bool IsSupportedVersion => Version == LiveV2ProtocolConstants.Version;
    public int TotalFrameLength => checked(LiveV2ProtocolConstants.FrameHeaderSize + (int)PayloadLength);
}

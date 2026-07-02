using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Sufni.Telemetry;

using Sufni.App.Acquisition.Services;
namespace Sufni.App.LiveDaq.Services.LiveStreaming;

// Incremental reader and writer helpers for the framed live protocol. Incomplete
// bytes stay buffered until a full header and payload are available.
public sealed class LiveV2ProtocolReader
{
    private readonly FramedMessageReader frameReader = new(
        LiveV2ProtocolConstants.FrameHeaderSize,
        static headerBytes => ParseHeader(headerBytes).TotalFrameLength);

    // Number of unread bytes currently buffered.
    public int BufferedByteCount => frameReader.BufferedByteCount;

    // Appends newly read socket bytes to the unread portion of the buffer.
    public void Append(ReadOnlySpan<byte> bytes)
    {
        frameReader.Append(bytes);
    }

    // Clears all unread buffered bytes.
    public void Reset()
    {
        frameReader.Reset();
    }

    // Tries to parse and consume exactly one complete frame. Returns false when more
    // bytes are needed and leaves the buffered data intact. Unknown frame types are
    // consumed and skipped so a newer-firmware frame never tears down the connection.
    public bool TryReadFrame(out LiveProtocolFrame? frame)
    {
        return frameReader.TryReadFrame(ParseKnownFrame, out frame);
    }

    private static LiveProtocolFrame? ParseKnownFrame(ReadOnlySpan<byte> frameBytes)
    {
        var header = ParseHeader(frameBytes[..LiveV2ProtocolConstants.FrameHeaderSize]);
        return IsKnownFrameType(header.FrameType) ? ParseFrame(frameBytes) : null;
    }

    private static bool IsKnownFrameType(LiveV2FrameType frameType) => frameType switch
    {
        LiveV2FrameType.StartLive => true,
        LiveV2FrameType.StopLive => true,
        LiveV2FrameType.Ping => true,
        LiveV2FrameType.Identify => true,
        LiveV2FrameType.StartLiveAck => true,
        LiveV2FrameType.StopLiveAck => true,
        LiveV2FrameType.Error => true,
        LiveV2FrameType.Pong => true,
        LiveV2FrameType.IdentifyAck => true,
        LiveV2FrameType.SessionHeader => true,
        LiveV2FrameType.TravelBatch => true,
        LiveV2FrameType.ImuBatch => true,
        LiveV2FrameType.GpsBatch => true,
        LiveV2FrameType.SessionStats => true,
        _ => false,
    };

    // Encodes a START_LIVE request using the exact wire payload layout.
    public static byte[] CreateStartLiveFrame(uint sequence, LiveStartRequest request)
    {
        var payload = new byte[LiveV2ProtocolConstants.StartRequestPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), (uint)request.RequestedSensorMask);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), LiveProtocolHelpers.MillihertzToV2WireHertz(request.TravelRateMhz));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), LiveProtocolHelpers.MillihertzToV2WireHertz(request.ImuRateMhz));
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), LiveProtocolHelpers.MillihertzToV2WireHertz(request.GpsRateMhz));
        return CreateFrame(LiveV2FrameType.StartLive, sequence, payload);
    }

    public static byte[] CreateStopLiveFrame(uint sequence) => CreateFrame(LiveV2FrameType.StopLive, sequence, ReadOnlySpan<byte>.Empty);

    public static byte[] CreatePingFrame(uint sequence) => CreateFrame(LiveV2FrameType.Ping, sequence, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateIdentifyFrame(uint sequence) => CreateFrame(LiveV2FrameType.Identify, sequence, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateFrame(LiveV2FrameType frameType, uint sequence, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[LiveV2ProtocolConstants.FrameHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), LiveV2ProtocolConstants.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), LiveV2ProtocolConstants.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), (ushort)frameType);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(12, 4), sequence);
        payload.CopyTo(frame.AsSpan(LiveV2ProtocolConstants.FrameHeaderSize));
        return frame;
    }

    // Parses one fully buffered frame and throws when the header or payload is invalid.
    public static LiveProtocolFrame ParseFrame(ReadOnlySpan<byte> frameBytes)
    {
        var header = ParseHeader(frameBytes[..LiveV2ProtocolConstants.FrameHeaderSize]);
        if (frameBytes.Length != header.TotalFrameLength)
        {
            throw new FormatException("Live frame length does not match payload length.");
        }

        var payload = frameBytes[LiveV2ProtocolConstants.FrameHeaderSize..];
        var metadata = CreateFrameMetadata(header);
        return header.FrameType switch
        {
            LiveV2FrameType.StartLive => new LiveStartRequestFrame(metadata, ParseStartRequest(payload)),
            LiveV2FrameType.StopLive => ParseEmptyPayloadFrame<LiveStopRequestFrame>(header, payload),
            LiveV2FrameType.Ping => ParseEmptyPayloadFrame<LivePingFrame>(header, payload),
            LiveV2FrameType.Identify => ParseEmptyPayloadFrame<LiveIdentifyRequestFrame>(header, payload),
            LiveV2FrameType.StartLiveAck => new LiveStartAckFrame(metadata, ParseStartAck(payload)),
            LiveV2FrameType.StopLiveAck => new LiveStopAckFrame(metadata, ParseStopAck(payload)),
            LiveV2FrameType.Error => new LiveErrorFrame(metadata, ParseError(payload)),
            LiveV2FrameType.Pong => ParseEmptyPayloadFrame<LivePongFrame>(header, payload),
            LiveV2FrameType.IdentifyAck => new LiveIdentifyAckFrame(metadata, ParseIdentifyAck(payload)),
            LiveV2FrameType.SessionHeader => new LiveSessionHeaderFrame(metadata, ParseSessionHeader(payload)),
            LiveV2FrameType.TravelBatch => ParseTravelBatchFrame(header, payload),
            LiveV2FrameType.ImuBatch => ParseImuBatchFrame(header, payload),
            LiveV2FrameType.GpsBatch => ParseGpsBatchFrame(header, payload),
            LiveV2FrameType.SessionStats => new LiveSessionStatsFrame(metadata, ParseSessionStats(payload)),
            _ => throw new FormatException($"Unsupported live frame type {(ushort)header.FrameType}.")
        };
    }

    // Parses and validates the fixed 16-byte live frame header.
    public static LiveV2FrameHeader ParseHeader(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length < LiveV2ProtocolConstants.FrameHeaderSize)
        {
            throw new FormatException("Live frame header is truncated.");
        }

        var header = new LiveV2FrameHeader(
            Magic: BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[0..4]),
            Version: BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[4..6]),
            FrameType: (LiveV2FrameType)BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[6..8]),
            PayloadLength: BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[8..12]),
            Sequence: BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[12..16]));

        if (!header.IsValidMagic)
        {
            throw new FormatException($"Live frame magic 0x{header.Magic:X8} is invalid.");
        }

        if (!header.IsSupportedVersion)
        {
            throw new FormatException($"Live protocol version {header.Version} is not supported.");
        }

        if (header.PayloadLength > LiveV2ProtocolConstants.MaxPayloadLength)
        {
            throw new FormatException(
                $"Live frame payload length {header.PayloadLength} exceeds maximum {LiveV2ProtocolConstants.MaxPayloadLength}.");
        }

        return header;
    }

    private static LiveStartRequest ParseStartRequest(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveV2ProtocolConstants.StartRequestPayloadSize, LiveV2FrameType.StartLive);
        return new LiveStartRequest(
            RequestedSensorMask: (LiveSensorInstanceMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]),
            TravelRateMhz: LiveProtocolHelpers.HertzToMillihertz(BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8])),
            ImuRateMhz: LiveProtocolHelpers.HertzToMillihertz(BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12])),
            GpsRateMhz: LiveProtocolHelpers.HertzToMillihertz(BinaryPrimitives.ReadUInt32LittleEndian(payload[12..16])));
    }

    private static LiveIdentifyAck ParseIdentifyAck(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveV2ProtocolConstants.IdentifyAckPayloadSize, LiveV2FrameType.IdentifyAck);
        return new LiveIdentifyAck(payload[..8].ToArray());
    }

    private static LiveStartAck ParseStartAck(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveV2ProtocolConstants.StartAckPayloadSize, LiveV2FrameType.StartLiveAck);
        return new LiveStartAck(
            Result: (LiveStartErrorCode)BinaryPrimitives.ReadInt32LittleEndian(payload[0..4]),
            SessionId: BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]),
            SelectedStreamMask: LiveProtocolHelpers.ToStreamMask((LiveV2StreamMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12])));
    }

    private static LiveSessionHeader ParseSessionHeader(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveV2ProtocolConstants.SessionHeaderPayloadSize, LiveV2FrameType.SessionHeader);

        return new LiveSessionHeader(
            SessionId: BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]),
            AcceptedTravelRateMhz: LiveProtocolHelpers.HertzToMillihertz(BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8])),
            AcceptedImuRateMhz: LiveProtocolHelpers.HertzToMillihertz(BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12])),
            AcceptedGpsRateMhz: LiveProtocolHelpers.HertzToMillihertz(BinaryPrimitives.ReadUInt32LittleEndian(payload[12..16])),
            SessionStartUtc: DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64LittleEndian(payload[16..24])),
            SessionStartMonotonicUs: BinaryPrimitives.ReadUInt64LittleEndian(payload[24..32]),
            ActiveImuMask: (LiveImuLocationMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[32..36]),
            ImuCalibrationScales: new LiveImuCalibrationScales(
                FrameAccelLsbPerG: ReadSingleLittleEndian(payload[36..40]),
                ForkAccelLsbPerG: ReadSingleLittleEndian(payload[40..44]),
                RearAccelLsbPerG: ReadSingleLittleEndian(payload[44..48]),
                FrameGyroLsbPerDps: ReadSingleLittleEndian(payload[48..52]),
                ForkGyroLsbPerDps: ReadSingleLittleEndian(payload[52..56]),
                RearGyroLsbPerDps: ReadSingleLittleEndian(payload[56..60])),
            Flags: (LiveSessionFlags)BinaryPrimitives.ReadUInt32LittleEndian(payload[60..64]),
            RequestedSensorMask: (LiveSensorInstanceMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[64..68]),
            AcceptedSensorMask: (LiveSensorInstanceMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[68..72]));
    }

    private static LiveStopAck ParseStopAck(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveV2ProtocolConstants.StopAckPayloadSize, LiveV2FrameType.StopLiveAck);
        return new LiveStopAck(BinaryPrimitives.ReadUInt32LittleEndian(payload));
    }

    private static LiveError ParseError(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveV2ProtocolConstants.ErrorPayloadSize, LiveV2FrameType.Error);
        return new LiveError((LiveStartErrorCode)BinaryPrimitives.ReadInt32LittleEndian(payload));
    }

    private static LiveTravelBatchFrame ParseTravelBatchFrame(LiveV2FrameHeader header, ReadOnlySpan<byte> payload)
    {
        var batch = ParseBatchHeader(payload, LiveStreamMask.Travel);
        var recordsData = payload[LiveV2ProtocolConstants.BatchHeaderSize..];
        if (recordsData.Length != batch.SampleCount * LiveV2ProtocolConstants.TravelRecordSize)
        {
            throw new FormatException("Travel batch payload length does not match the record count.");
        }

        var records = new List<LiveTravelRecord>((int)batch.SampleCount);
        for (var offset = 0; offset < recordsData.Length; offset += LiveV2ProtocolConstants.TravelRecordSize)
        {
            records.Add(new LiveTravelRecord(
                ForkAngle: BinaryPrimitives.ReadUInt16LittleEndian(recordsData[offset..(offset + 2)]),
                ShockAngle: BinaryPrimitives.ReadUInt16LittleEndian(recordsData[(offset + 2)..(offset + 4)])));
        }

        return new LiveTravelBatchFrame(CreateFrameMetadata(header), batch, records);
    }

    private static LiveImuBatchFrame ParseImuBatchFrame(LiveV2FrameHeader header, ReadOnlySpan<byte> payload)
    {
        var batch = ParseBatchHeader(payload, LiveStreamMask.Imu);
        var recordsData = payload[LiveV2ProtocolConstants.BatchHeaderSize..];
        if (recordsData.Length % LiveV2ProtocolConstants.ImuRecordSize != 0)
        {
            throw new FormatException("IMU batch payload length is not aligned to IMU record size.");
        }

        var records = new List<ImuRecord>(recordsData.Length / LiveV2ProtocolConstants.ImuRecordSize);
        for (var offset = 0; offset < recordsData.Length; offset += LiveV2ProtocolConstants.ImuRecordSize)
        {
            records.Add(new ImuRecord(
                Ax: BinaryPrimitives.ReadInt16LittleEndian(recordsData[offset..(offset + 2)]),
                Ay: BinaryPrimitives.ReadInt16LittleEndian(recordsData[(offset + 2)..(offset + 4)]),
                Az: BinaryPrimitives.ReadInt16LittleEndian(recordsData[(offset + 4)..(offset + 6)]),
                Gx: BinaryPrimitives.ReadInt16LittleEndian(recordsData[(offset + 6)..(offset + 8)]),
                Gy: BinaryPrimitives.ReadInt16LittleEndian(recordsData[(offset + 8)..(offset + 10)]),
                Gz: BinaryPrimitives.ReadInt16LittleEndian(recordsData[(offset + 10)..(offset + 12)])));
        }

        return new LiveImuBatchFrame(CreateFrameMetadata(header), batch, records);
    }

    private static LiveGpsBatchFrame ParseGpsBatchFrame(LiveV2FrameHeader header, ReadOnlySpan<byte> payload)
    {
        var batch = ParseBatchHeader(payload, LiveStreamMask.Gps);
        var recordsData = payload[LiveV2ProtocolConstants.BatchHeaderSize..];
        if (recordsData.Length != batch.SampleCount * LiveV2ProtocolConstants.GpsRecordSize)
        {
            throw new FormatException("GPS batch payload length does not match the record count.");
        }

        var records = new List<GpsRecord>((int)batch.SampleCount);
        for (var offset = 0; offset < recordsData.Length; offset += LiveV2ProtocolConstants.GpsRecordSize)
        {
            var record = GpsBinaryRecordDecoder.Decode(recordsData.Slice(offset, LiveV2ProtocolConstants.GpsRecordSize));
            if (record is not null)
                records.Add(record);
        }

        return new LiveGpsBatchFrame(CreateFrameMetadata(header), batch, records);
    }

    private static LiveSessionStats ParseSessionStats(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveV2ProtocolConstants.SessionStatsPayloadSize, LiveV2FrameType.SessionStats);
        return new LiveSessionStats(
            SessionId: BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]),
            TravelQueueDepth: BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]),
            ImuQueueDepth: BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]),
            GpsQueueDepth: BinaryPrimitives.ReadUInt32LittleEndian(payload[12..16]),
            TravelDroppedBatches: BinaryPrimitives.ReadUInt32LittleEndian(payload[16..20]),
            ImuDroppedBatches: BinaryPrimitives.ReadUInt32LittleEndian(payload[20..24]),
            GpsDroppedBatches: BinaryPrimitives.ReadUInt32LittleEndian(payload[24..28]));
    }

    private static LiveBatchHeader ParseBatchHeader(ReadOnlySpan<byte> payload, LiveStreamMask stream)
    {
        if (payload.Length < LiveV2ProtocolConstants.BatchHeaderSize)
        {
            throw new FormatException("Batch payload is shorter than the batch header.");
        }

        return new LiveBatchHeader(
            SessionId: BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]),
            Stream: stream,
            StreamSequence: BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]),
            FirstIndex: BinaryPrimitives.ReadUInt64LittleEndian(payload[8..16]),
            FirstMonotonicDeltaUs: 0,
            FirstMonotonicUs: BinaryPrimitives.ReadUInt64LittleEndian(payload[16..24]),
            SampleCount: BinaryPrimitives.ReadUInt32LittleEndian(payload[24..28]),
            ValidityMask: LiveSensorInstanceMask.All);
    }

    private static T ParseEmptyPayloadFrame<T>(LiveV2FrameHeader header, ReadOnlySpan<byte> payload)
        where T : LiveProtocolFrame
    {
        if (payload.Length != 0)
        {
            throw new FormatException($"{header.FrameType} should not carry a payload.");
        }

        return (T)Activator.CreateInstance(typeof(T), CreateFrameMetadata(header))!;
    }

    private static LiveFrameMetadata CreateFrameMetadata(LiveV2FrameHeader header) => new(header.Sequence);

    private static void EnsurePayloadLength(ReadOnlySpan<byte> payload, int expectedLength, LiveV2FrameType frameType)
    {
        if (payload.Length != expectedLength)
        {
            throw new FormatException($"{frameType} payload length {payload.Length} does not match expected length {expectedLength}.");
        }
    }

    private static float ReadSingleLittleEndian(ReadOnlySpan<byte> bytes)
    {
        var raw = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        return BitConverter.Int32BitsToSingle(raw);
    }

}

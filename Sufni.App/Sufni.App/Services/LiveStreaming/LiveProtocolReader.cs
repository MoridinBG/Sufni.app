using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Sufni.Telemetry;
using Sufni.App.Services;

namespace Sufni.App.Services.LiveStreaming;

// Incremental reader and writer helpers for the framed live protocol. Incomplete
// bytes stay buffered until a full header and payload are available.
public sealed class LiveProtocolReader
{
    private readonly UnreadByteBuffer unreadBytes = new();

    // Number of unread bytes currently buffered.
    public int BufferedByteCount => unreadBytes.BufferedByteCount;

    // Appends newly read socket bytes to the unread portion of the buffer.
    public void Append(ReadOnlySpan<byte> bytes)
    {
        unreadBytes.Append(bytes);
    }

    // Clears all unread buffered bytes.
    public void Reset()
    {
        unreadBytes.Reset();
    }

    // Tries to parse and consume exactly one complete frame. Returns false when more
    // bytes are needed and leaves the buffered data intact. Unknown frame types are
    // consumed and skipped so a newer-firmware frame never tears down the connection.
    public bool TryReadFrame(out LiveProtocolFrame? frame)
    {
        while (true)
        {
            frame = null;
            if (unreadBytes.BufferedByteCount < LiveProtocolConstants.FrameHeaderSize)
            {
                return false;
            }

            var pendingSpan = unreadBytes.UnreadBytes;
            var header = ParseHeader(pendingSpan[..LiveProtocolConstants.FrameHeaderSize]);
            var totalLength = header.TotalFrameLength;
            if (unreadBytes.BufferedByteCount < totalLength)
            {
                return false;
            }

            if (!IsKnownFrameType(header.FrameType))
            {
                unreadBytes.Consume(totalLength);
                continue;
            }

            frame = ParseFrame(pendingSpan[..totalLength]);
            unreadBytes.Consume(totalLength);
            return true;
        }
    }

    private static bool IsKnownFrameType(LiveFrameType frameType) => frameType switch
    {
        LiveFrameType.StartLive => true,
        LiveFrameType.StopLive => true,
        LiveFrameType.Ping => true,
        LiveFrameType.Identify => true,
        LiveFrameType.StartLiveAck => true,
        LiveFrameType.StopLiveAck => true,
        LiveFrameType.Error => true,
        LiveFrameType.Pong => true,
        LiveFrameType.IdentifyAck => true,
        LiveFrameType.SessionHeader => true,
        LiveFrameType.TravelBatch => true,
        LiveFrameType.ImuBatch => true,
        LiveFrameType.GpsBatch => true,
        LiveFrameType.SessionStats => true,
        _ => false,
    };

    // Encodes a START_LIVE request using the exact wire payload layout.
    public static byte[] CreateStartLiveFrame(uint sequence, LiveStartRequest request)
    {
        var payload = new byte[LiveProtocolConstants.StartRequestPayloadSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), (uint)request.RequestedSensorMask);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), request.TravelHz);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), request.ImuHz);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), request.GpsFixHz);
        return CreateFrame(LiveFrameType.StartLive, sequence, payload);
    }

    public static byte[] CreateStopLiveFrame(uint sequence) => CreateFrame(LiveFrameType.StopLive, sequence, ReadOnlySpan<byte>.Empty);

    public static byte[] CreatePingFrame(uint sequence) => CreateFrame(LiveFrameType.Ping, sequence, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateIdentifyFrame(uint sequence) => CreateFrame(LiveFrameType.Identify, sequence, ReadOnlySpan<byte>.Empty);

    public static byte[] CreateFrame(LiveFrameType frameType, uint sequence, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[LiveProtocolConstants.FrameHeaderSize + payload.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(0, 4), LiveProtocolConstants.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), LiveProtocolConstants.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(6, 2), (ushort)frameType);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(12, 4), sequence);
        payload.CopyTo(frame.AsSpan(LiveProtocolConstants.FrameHeaderSize));
        return frame;
    }

    // Parses one fully buffered frame and throws when the header or payload is invalid.
    public static LiveProtocolFrame ParseFrame(ReadOnlySpan<byte> frameBytes)
    {
        var header = ParseHeader(frameBytes[..LiveProtocolConstants.FrameHeaderSize]);
        if (frameBytes.Length != header.TotalFrameLength)
        {
            throw new FormatException("Live frame length does not match payload length.");
        }

        var payload = frameBytes[LiveProtocolConstants.FrameHeaderSize..];
        return header.FrameType switch
        {
            LiveFrameType.StartLive => new LiveStartRequestFrame(header, ParseStartRequest(payload)),
            LiveFrameType.StopLive => ParseEmptyPayloadFrame<LiveStopRequestFrame>(header, payload),
            LiveFrameType.Ping => ParseEmptyPayloadFrame<LivePingFrame>(header, payload),
            LiveFrameType.Identify => ParseEmptyPayloadFrame<LiveIdentifyRequestFrame>(header, payload),
            LiveFrameType.StartLiveAck => new LiveStartAckFrame(header, ParseStartAck(payload)),
            LiveFrameType.StopLiveAck => new LiveStopAckFrame(header, ParseStopAck(payload)),
            LiveFrameType.Error => new LiveErrorFrame(header, ParseError(payload)),
            LiveFrameType.Pong => ParseEmptyPayloadFrame<LivePongFrame>(header, payload),
            LiveFrameType.IdentifyAck => new LiveIdentifyAckFrame(header, ParseIdentifyAck(payload)),
            LiveFrameType.SessionHeader => new LiveSessionHeaderFrame(header, ParseSessionHeader(payload)),
            LiveFrameType.TravelBatch => ParseTravelBatchFrame(header, payload),
            LiveFrameType.ImuBatch => ParseImuBatchFrame(header, payload),
            LiveFrameType.GpsBatch => ParseGpsBatchFrame(header, payload),
            LiveFrameType.SessionStats => new LiveSessionStatsFrame(header, ParseSessionStats(payload)),
            _ => throw new FormatException($"Unsupported live frame type {(ushort)header.FrameType}.")
        };
    }

    // Parses and validates the fixed 16-byte live frame header.
    public static LiveFrameHeader ParseHeader(ReadOnlySpan<byte> headerBytes)
    {
        if (headerBytes.Length < LiveProtocolConstants.FrameHeaderSize)
        {
            throw new FormatException("Live frame header is truncated.");
        }

        var header = new LiveFrameHeader(
            Magic: BinaryPrimitives.ReadUInt32LittleEndian(headerBytes[0..4]),
            Version: BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[4..6]),
            FrameType: (LiveFrameType)BinaryPrimitives.ReadUInt16LittleEndian(headerBytes[6..8]),
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

        if (header.PayloadLength > LiveProtocolConstants.MaxPayloadLength)
        {
            throw new FormatException(
                $"Live frame payload length {header.PayloadLength} exceeds maximum {LiveProtocolConstants.MaxPayloadLength}.");
        }

        return header;
    }

    private static LiveStartRequest ParseStartRequest(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveProtocolConstants.StartRequestPayloadSize, LiveFrameType.StartLive);
        return new LiveStartRequest(
            RequestedSensorMask: (LiveSensorInstanceMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]),
            TravelHz: BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]),
            ImuHz: BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]),
            GpsFixHz: BinaryPrimitives.ReadUInt32LittleEndian(payload[12..16]));
    }

    private static LiveIdentifyAck ParseIdentifyAck(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveProtocolConstants.IdentifyAckPayloadSize, LiveFrameType.IdentifyAck);
        return new LiveIdentifyAck(payload[..8].ToArray());
    }

    private static LiveStartAck ParseStartAck(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveProtocolConstants.StartAckPayloadSize, LiveFrameType.StartLiveAck);
        return new LiveStartAck(
            Result: (LiveStartErrorCode)BinaryPrimitives.ReadInt32LittleEndian(payload[0..4]),
            SessionId: BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]),
            SelectedSensorMask: (LiveSensorMask)BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]));
    }

    private static LiveSessionHeader ParseSessionHeader(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveProtocolConstants.SessionHeaderPayloadSize, LiveFrameType.SessionHeader);

        return new LiveSessionHeader(
            SessionId: BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]),
            AcceptedTravelHz: BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]),
            AcceptedImuHz: BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]),
            AcceptedGpsFixHz: BinaryPrimitives.ReadUInt32LittleEndian(payload[12..16]),
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
        EnsurePayloadLength(payload, LiveProtocolConstants.StopAckPayloadSize, LiveFrameType.StopLiveAck);
        return new LiveStopAck(BinaryPrimitives.ReadUInt32LittleEndian(payload));
    }

    private static LiveError ParseError(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveProtocolConstants.ErrorPayloadSize, LiveFrameType.Error);
        return new LiveError((LiveStartErrorCode)BinaryPrimitives.ReadInt32LittleEndian(payload));
    }

    private static LiveTravelBatchFrame ParseTravelBatchFrame(LiveFrameHeader header, ReadOnlySpan<byte> payload)
    {
        var batch = ParseBatchHeader(payload);
        var recordsData = payload[LiveProtocolConstants.BatchHeaderSize..];
        if (recordsData.Length != batch.SampleCount * LiveProtocolConstants.TravelRecordSize)
        {
            throw new FormatException("Travel batch payload length does not match the record count.");
        }

        var records = new List<LiveTravelRecord>((int)batch.SampleCount);
        for (var offset = 0; offset < recordsData.Length; offset += LiveProtocolConstants.TravelRecordSize)
        {
            records.Add(new LiveTravelRecord(
                ForkAngle: BinaryPrimitives.ReadUInt16LittleEndian(recordsData[offset..(offset + 2)]),
                ShockAngle: BinaryPrimitives.ReadUInt16LittleEndian(recordsData[(offset + 2)..(offset + 4)])));
        }

        return new LiveTravelBatchFrame(header, batch, records);
    }

    private static LiveImuBatchFrame ParseImuBatchFrame(LiveFrameHeader header, ReadOnlySpan<byte> payload)
    {
        var batch = ParseBatchHeader(payload);
        var recordsData = payload[LiveProtocolConstants.BatchHeaderSize..];
        if (recordsData.Length % LiveProtocolConstants.ImuRecordSize != 0)
        {
            throw new FormatException("IMU batch payload length is not aligned to IMU record size.");
        }

        var records = new List<ImuRecord>(recordsData.Length / LiveProtocolConstants.ImuRecordSize);
        for (var offset = 0; offset < recordsData.Length; offset += LiveProtocolConstants.ImuRecordSize)
        {
            records.Add(new ImuRecord(
                Ax: BinaryPrimitives.ReadInt16LittleEndian(recordsData[offset..(offset + 2)]),
                Ay: BinaryPrimitives.ReadInt16LittleEndian(recordsData[(offset + 2)..(offset + 4)]),
                Az: BinaryPrimitives.ReadInt16LittleEndian(recordsData[(offset + 4)..(offset + 6)]),
                Gx: BinaryPrimitives.ReadInt16LittleEndian(recordsData[(offset + 6)..(offset + 8)]),
                Gy: BinaryPrimitives.ReadInt16LittleEndian(recordsData[(offset + 8)..(offset + 10)]),
                Gz: BinaryPrimitives.ReadInt16LittleEndian(recordsData[(offset + 10)..(offset + 12)])));
        }

        return new LiveImuBatchFrame(header, batch, records);
    }

    private static LiveGpsBatchFrame ParseGpsBatchFrame(LiveFrameHeader header, ReadOnlySpan<byte> payload)
    {
        var batch = ParseBatchHeader(payload);
        var recordsData = payload[LiveProtocolConstants.BatchHeaderSize..];
        if (recordsData.Length != batch.SampleCount * LiveProtocolConstants.GpsRecordSize)
        {
            throw new FormatException("GPS batch payload length does not match the record count.");
        }

        var records = new List<GpsRecord>((int)batch.SampleCount);
        for (var offset = 0; offset < recordsData.Length; offset += LiveProtocolConstants.GpsRecordSize)
        {
            var date = BinaryPrimitives.ReadUInt32LittleEndian(recordsData[offset..(offset + 4)]);
            var timeMs = BinaryPrimitives.ReadUInt32LittleEndian(recordsData[(offset + 4)..(offset + 8)]);
            var latitude = BitConverter.ToDouble(recordsData.Slice(offset + 8, 8));
            var longitude = BitConverter.ToDouble(recordsData.Slice(offset + 16, 8));
            var altitude = ReadSingleLittleEndian(recordsData[(offset + 24)..(offset + 28)]);
            var speed = ReadSingleLittleEndian(recordsData[(offset + 28)..(offset + 32)]);
            var heading = ReadSingleLittleEndian(recordsData[(offset + 32)..(offset + 36)]);
            var fixMode = recordsData[offset + 36];
            var satellites = recordsData[offset + 37];
            var epe2d = ReadSingleLittleEndian(recordsData[(offset + 38)..(offset + 42)]);
            var epe3d = ReadSingleLittleEndian(recordsData[(offset + 42)..(offset + 46)]);

            if (!TryCreateGpsTimestamp(date, timeMs, out var timestamp))
            {
                continue;
            }

            records.Add(new GpsRecord(timestamp, latitude, longitude, altitude, speed, heading, fixMode, satellites, epe2d, epe3d));
        }

        return new LiveGpsBatchFrame(header, batch, records);
    }

    private static LiveSessionStats ParseSessionStats(ReadOnlySpan<byte> payload)
    {
        EnsurePayloadLength(payload, LiveProtocolConstants.SessionStatsPayloadSize, LiveFrameType.SessionStats);
        return new LiveSessionStats(
            SessionId: BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]),
            TravelQueueDepth: BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]),
            ImuQueueDepth: BinaryPrimitives.ReadUInt32LittleEndian(payload[8..12]),
            GpsQueueDepth: BinaryPrimitives.ReadUInt32LittleEndian(payload[12..16]),
            TravelDroppedBatches: BinaryPrimitives.ReadUInt32LittleEndian(payload[16..20]),
            ImuDroppedBatches: BinaryPrimitives.ReadUInt32LittleEndian(payload[20..24]),
            GpsDroppedBatches: BinaryPrimitives.ReadUInt32LittleEndian(payload[24..28]));
    }

    private static LiveBatchHeader ParseBatchHeader(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < LiveProtocolConstants.BatchHeaderSize)
        {
            throw new FormatException("Batch payload is shorter than the batch header.");
        }

        return new LiveBatchHeader(
            SessionId: BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]),
            StreamSequence: BinaryPrimitives.ReadUInt32LittleEndian(payload[4..8]),
            FirstIndex: BinaryPrimitives.ReadUInt64LittleEndian(payload[8..16]),
            FirstMonotonicUs: BinaryPrimitives.ReadUInt64LittleEndian(payload[16..24]),
            SampleCount: BinaryPrimitives.ReadUInt32LittleEndian(payload[24..28]));
    }

    private static T ParseEmptyPayloadFrame<T>(LiveFrameHeader header, ReadOnlySpan<byte> payload)
        where T : LiveProtocolFrame
    {
        if (payload.Length != 0)
        {
            throw new FormatException($"{header.FrameType} should not carry a payload.");
        }

        return (T)Activator.CreateInstance(typeof(T), header)!;
    }

    private static void EnsurePayloadLength(ReadOnlySpan<byte> payload, int expectedLength, LiveFrameType frameType)
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

    // Firmware emits date=0 before the GPS module has a fix. Skip those records
    // instead of throwing from the DateTime constructor and tearing the stream down.
    private static bool TryCreateGpsTimestamp(uint date, uint timeMs, out DateTime timestamp)
    {
        timestamp = default;
        if (date == 0)
        {
            return false;
        }

        var year = (int)(date / 10000);
        var month = (int)(date / 100 % 100);
        var day = (int)(date % 100);

        if (year is < 1 or > 9999 || month is < 1 or > 12)
        {
            return false;
        }

        if (day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        timestamp = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(timeMs);
        return true;
    }
}
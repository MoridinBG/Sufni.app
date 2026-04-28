using System.Buffers.Binary;
using Sufni.App.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services.LiveStreaming;

public class LiveProtocolReaderTests
{
    [Fact]
    public void TryReadFrame_WaitsForCompleteHeaderAndPayloadAcrossReads()
    {
        var reader = new LiveProtocolReader();
        var frameBytes = LiveProtocolTestFrames.CreateStartAckFrame(
            sequence: 5,
            result: LiveStartErrorCode.Busy,
            sessionId: 0,
            selectedSensorMask: LiveSensorMask.None);

        reader.Append(frameBytes.AsSpan(0, 10));
        Assert.False(reader.TryReadFrame(out var frame));
        Assert.Null(frame);

        reader.Append(frameBytes.AsSpan(10, 12));
        Assert.False(reader.TryReadFrame(out frame));
        Assert.Null(frame);

        reader.Append(frameBytes.AsSpan(22));
        Assert.True(reader.TryReadFrame(out frame));

        var ackFrame = Assert.IsType<LiveStartAckFrame>(frame);
        Assert.Equal((uint)5, ackFrame.Sequence);
        Assert.Equal(LiveStartErrorCode.Busy, ackFrame.Payload.Result);
        Assert.Equal(0, reader.BufferedByteCount);
    }

    [Fact]
    public void TryReadFrame_PreservesUnreadBytesAcrossFrameConsumptionAndLaterAppend()
    {
        var reader = new LiveProtocolReader();
        var firstFrameBytes = LiveProtocolTestFrames.CreateStartAckFrame(sequence: 1, result: LiveStartErrorCode.Ok, sessionId: 501, selectedSensorMask: LiveSensorMask.Travel);
        var secondFrameBytes = LiveProtocolTestFrames.CreateStartAckFrame(sequence: 2, result: LiveStartErrorCode.Busy, sessionId: 0, selectedSensorMask: LiveSensorMask.None);

        reader.Append(firstFrameBytes);
        reader.Append(secondFrameBytes.AsSpan(0, 8));

        Assert.True(reader.TryReadFrame(out var firstFrame));
        var firstAck = Assert.IsType<LiveStartAckFrame>(firstFrame);
        Assert.Equal((uint)1, firstAck.Sequence);
        Assert.Equal(8, reader.BufferedByteCount);

        reader.Append(secondFrameBytes.AsSpan(8));

        Assert.True(reader.TryReadFrame(out var secondFrame));
        var secondAck = Assert.IsType<LiveStartAckFrame>(secondFrame);
        Assert.Equal((uint)2, secondAck.Sequence);
        Assert.Equal(LiveStartErrorCode.Busy, secondAck.Payload.Result);
        Assert.Equal(0, reader.BufferedByteCount);
    }

    [Fact]
    public void TryReadFrame_ThrowsWhenHeaderMagicIsInvalid()
    {
        var reader = new LiveProtocolReader();
        var frameBytes = LiveProtocolTestFrames.CreateStartAckFrame(sequence: 1);
        frameBytes[0] = 0;

        reader.Append(frameBytes);

        Assert.Throws<FormatException>(() => reader.TryReadFrame(out _));
    }

    [Fact]
    public void ParseFrame_ReturnsIdentifyAckFrame_WithBoardSerial()
    {
        var boardSerial = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0x11, 0x22, 0x33, 0x44 };
        var frameBytes = LiveProtocolTestFrames.CreateIdentifyAckFrame(sequence: 3, boardSerial: boardSerial);

        var frame = Assert.IsType<LiveIdentifyAckFrame>(LiveProtocolReader.ParseFrame(frameBytes));

        Assert.Equal((uint)3, frame.Sequence);
        Assert.Equal(boardSerial, frame.Payload.BoardSerial);
    }

    [Fact]
    public void ParseFrame_ReturnsSessionHeaderFrame_WithCalibrationAndImuLocations()
    {
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(
            requestedSensorMask: LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu,
            acceptedSensorMask: LiveSensorInstanceMask.ForkTravel | LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.RearImu);
        var frameBytes = LiveProtocolTestFrames.CreateSessionHeaderFrame(7, sessionHeader);

        var frame = Assert.IsType<LiveSessionHeaderFrame>(LiveProtocolReader.ParseFrame(frameBytes));

        Assert.Equal(sessionHeader.SessionId, frame.Payload.SessionId);
        Assert.Equal(sessionHeader.ActiveImuMask, frame.Payload.ActiveImuMask);
        Assert.Equal(sessionHeader.RequestedSensorMask, frame.Payload.RequestedSensorMask);
        Assert.Equal(sessionHeader.AcceptedSensorMask, frame.Payload.AcceptedSensorMask);
        Assert.Equal(LiveSensorInstanceMask.ShockTravel | LiveSensorInstanceMask.ForkImu, frame.Payload.MissingSensorMask);
        Assert.Equal(sessionHeader.ImuCalibrationScales.FrameAccelLsbPerG, frame.Payload.ImuCalibrationScales.FrameAccelLsbPerG);
        Assert.Equal(sessionHeader.ImuCalibrationScales.RearGyroLsbPerDps, frame.Payload.ImuCalibrationScales.RearGyroLsbPerDps);
        Assert.Equal(new[] { LiveImuLocation.Frame, LiveImuLocation.Rear }, frame.Payload.GetActiveImuLocations());
    }

    [Fact]
    public void CreateStartLiveFrame_WritesRequestedSensorInstanceMask()
    {
        var request = new LiveStartRequest(
            LiveSensorInstanceMask.ForkTravel | LiveSensorInstanceMask.RearImu | LiveSensorInstanceMask.Gps,
            TravelHz: 100,
            ImuHz: 200,
            GpsFixHz: 10);

        var frameBytes = LiveProtocolReader.CreateStartLiveFrame(12, request);

        Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(frameBytes.AsSpan(4, 2)));
        Assert.Equal((uint)LiveSensorInstanceMask.ForkTravel | (uint)LiveSensorInstanceMask.RearImu | (uint)LiveSensorInstanceMask.Gps, BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.AsSpan(16, 4)));
        Assert.Equal((uint)100, BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.AsSpan(20, 4)));
        Assert.Equal((uint)200, BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.AsSpan(24, 4)));
        Assert.Equal((uint)10, BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.AsSpan(28, 4)));
    }

    [Fact]
    public void TryReadFrame_ThrowsWhenPayloadLengthExceedsMaximum()
    {
        var reader = new LiveProtocolReader();
        var header = CreateRawFrameHeader(
            frameType: (LiveFrameType)0xFFFF,
            payloadLength: (uint)(LiveProtocolConstants.MaxPayloadLength + 1),
            sequence: 1);

        reader.Append(header);

        Assert.Throws<FormatException>(() => reader.TryReadFrame(out _));
    }

    [Fact]
    public void TryReadFrame_SkipsUnknownFrameType_AndReturnsNextKnownFrame()
    {
        var reader = new LiveProtocolReader();
        var unknownFrame = CreateRawFrameHeader(
            frameType: (LiveFrameType)0xFFFF,
            payloadLength: 0,
            sequence: 1);
        var knownFrame = LiveProtocolTestFrames.CreateStartAckFrame(sequence: 2, result: LiveStartErrorCode.Ok);

        reader.Append(unknownFrame);
        reader.Append(knownFrame);

        Assert.True(reader.TryReadFrame(out var frame));
        var ack = Assert.IsType<LiveStartAckFrame>(frame);
        Assert.Equal((uint)2, ack.Sequence);
        Assert.Equal(0, reader.BufferedByteCount);
    }

    [Fact]
    public void ParseFrame_GpsBatch_SkipsRecordsWithInvalidDate()
    {
        var valid = new GpsRecord(
            Timestamp: new DateTime(2026, 3, 14, 12, 34, 56, 789, DateTimeKind.Utc),
            Latitude: 48.2082,
            Longitude: 16.3738,
            Altitude: 182.5f,
            Speed: 7.25f,
            Heading: 128.5f,
            FixMode: 2,
            Satellites: 10,
            Epe2d: 1.1f,
            Epe3d: 2.2f);

        var frameBytes = CreateGpsBatchFrameWithInvalidLeadingRecord(
            sequence: 9,
            sessionId: 88,
            validRecord: valid);

        var frame = Assert.IsType<LiveGpsBatchFrame>(LiveProtocolReader.ParseFrame(frameBytes));

        var decoded = Assert.Single(frame.Records);
        Assert.Equal(valid.Timestamp, decoded.Timestamp);
        Assert.Equal(valid.Latitude, decoded.Latitude);
    }

    [Fact]
    public void ParseFrame_ReturnsGpsBatchFrame_WithDecodedGpsRecord()
    {
        var record = new GpsRecord(
            Timestamp: new DateTime(2026, 3, 14, 12, 34, 56, 789, DateTimeKind.Utc),
            Latitude: 48.2082,
            Longitude: 16.3738,
            Altitude: 182.5f,
            Speed: 7.25f,
            Heading: 128.5f,
            FixMode: 2,
            Satellites: 10,
            Epe2d: 1.1f,
            Epe3d: 2.2f);
        var frameBytes = LiveProtocolTestFrames.CreateGpsBatchFrame(sequence: 9, sessionId: 88, record: record);

        var frame = Assert.IsType<LiveGpsBatchFrame>(LiveProtocolReader.ParseFrame(frameBytes));

        Assert.Equal((uint)88, frame.Batch.SessionId);
        var decoded = Assert.Single(frame.Records);
        Assert.Equal(record.Timestamp, decoded.Timestamp);
        Assert.Equal(record.Latitude, decoded.Latitude);
        Assert.Equal(record.Longitude, decoded.Longitude);
        Assert.Equal(record.FixMode, decoded.FixMode);
        Assert.Equal(record.Satellites, decoded.Satellites);
    }

    private static byte[] CreateRawFrameHeader(LiveFrameType frameType, uint payloadLength, uint sequence)
    {
        var header = new byte[LiveProtocolConstants.FrameHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), LiveProtocolConstants.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), LiveProtocolConstants.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), (ushort)frameType);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), payloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), sequence);
        return header;
    }

    private static byte[] CreateGpsBatchFrameWithInvalidLeadingRecord(uint sequence, uint sessionId, GpsRecord validRecord)
    {
        const int records = 2;
        var payload = new byte[LiveProtocolConstants.BatchHeaderSize + records * LiveProtocolConstants.GpsRecordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), sessionId);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(8, 8), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(16, 8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(24, 4), records);

        // First record: date=0, simulating no-fix firmware output.
        // Record bytes are already zeroed by allocation.

        var validOffset = LiveProtocolConstants.BatchHeaderSize + LiveProtocolConstants.GpsRecordSize;
        var timestamp = validRecord.Timestamp.ToUniversalTime();
        var date = (uint)(timestamp.Year * 10000 + timestamp.Month * 100 + timestamp.Day);
        var timeMs = (uint)timestamp.TimeOfDay.TotalMilliseconds;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(validOffset + 0, 4), date);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(validOffset + 4, 4), timeMs);
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(validOffset + 8, 8), BitConverter.DoubleToInt64Bits(validRecord.Latitude));
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(validOffset + 16, 8), BitConverter.DoubleToInt64Bits(validRecord.Longitude));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(validOffset + 24, 4), BitConverter.SingleToInt32Bits(validRecord.Altitude));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(validOffset + 28, 4), BitConverter.SingleToInt32Bits(validRecord.Speed));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(validOffset + 32, 4), BitConverter.SingleToInt32Bits(validRecord.Heading));
        payload[validOffset + 36] = validRecord.FixMode;
        payload[validOffset + 37] = validRecord.Satellites;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(validOffset + 38, 4), BitConverter.SingleToInt32Bits(validRecord.Epe2d));
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(validOffset + 42, 4), BitConverter.SingleToInt32Bits(validRecord.Epe3d));

        return LiveProtocolReader.CreateFrame(LiveFrameType.GpsBatch, sequence, payload);
    }
}
using System.Buffers.Binary;
using Sufni.Telemetry;

using Sufni.App.LiveDaq.Services.LiveStreaming;
namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveV2ProtocolReaderTests
{
    [Fact]
    public void TryReadFrame_WaitsForCompleteHeaderAndPayloadAcrossReads()
    {
        var reader = new LiveV2ProtocolReader();
        var frameBytes = LiveProtocolTestFrames.CreateStartAckFrame(
            sequence: 5,
            result: LiveStartErrorCode.Busy,
            sessionId: 0,
            selectedStreamMask: LiveStreamMask.None);

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
        var reader = new LiveV2ProtocolReader();
        var firstFrameBytes = LiveProtocolTestFrames.CreateStartAckFrame(sequence: 1, result: LiveStartErrorCode.Ok, sessionId: 501, selectedStreamMask: LiveStreamMask.Travel);
        var secondFrameBytes = LiveProtocolTestFrames.CreateStartAckFrame(sequence: 2, result: LiveStartErrorCode.Busy, sessionId: 0, selectedStreamMask: LiveStreamMask.None);

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
        var reader = new LiveV2ProtocolReader();
        var frameBytes = LiveProtocolTestFrames.CreateStartAckFrame(sequence: 1);
        frameBytes[0] = 0;

        reader.Append(frameBytes);

        Assert.Throws<FormatException>(() => reader.TryReadFrame(out _));
    }

    [Fact]
    public void ParseFrame_ReturnsIdentifyAckFrame_WithBoardSerial()
    {
        byte[] boardSerial = [0xAA, 0xBB, 0xCC, 0xDD, 0x11, 0x22, 0x33, 0x44];
        var frameBytes = LiveProtocolTestFrames.CreateIdentifyAckFrame(sequence: 3, boardSerial: boardSerial);

        var frame = Assert.IsType<LiveIdentifyAckFrame>(LiveV2ProtocolReader.ParseFrame(frameBytes));

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

        var frame = Assert.IsType<LiveSessionHeaderFrame>(LiveV2ProtocolReader.ParseFrame(frameBytes));

        Assert.Equal(sessionHeader.SessionId, frame.Payload.SessionId);
        Assert.Equal(LiveProtocolVersion.V2, frame.Payload.ProtocolVersion);
        Assert.Equal(sessionHeader.AcceptedTravelRateMhz, frame.Payload.AcceptedTravelRateMhz);
        Assert.Equal(sessionHeader.AcceptedImuRateMhz, frame.Payload.AcceptedImuRateMhz);
        Assert.Equal(sessionHeader.AcceptedGpsRateMhz, frame.Payload.AcceptedGpsRateMhz);
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
            TravelRateMhz: 100_000,
            ImuRateMhz: 200_500,
            GpsRateMhz: 10_000);

        var frameBytes = LiveV2ProtocolReader.CreateStartLiveFrame(12, request);

        Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(frameBytes.AsSpan(4, 2)));
        Assert.Equal((uint)LiveSensorInstanceMask.ForkTravel | (uint)LiveSensorInstanceMask.RearImu | (uint)LiveSensorInstanceMask.Gps, BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.AsSpan(16, 4)));
        Assert.Equal((uint)100, BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.AsSpan(20, 4)));
        Assert.Equal((uint)201, BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.AsSpan(24, 4)));
        Assert.Equal((uint)10, BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.AsSpan(28, 4)));
    }

    [Fact]
    public void CreateStartLiveFrame_WritesZeroForSubHzRequestedRates()
    {
        var request = new LiveStartRequest(
            LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu | LiveSensorInstanceMask.Gps,
            TravelRateMhz: 999,
            ImuRateMhz: 500,
            GpsRateMhz: 1);

        var frameBytes = LiveV2ProtocolReader.CreateStartLiveFrame(12, request);

        Assert.Equal((uint)0, BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.AsSpan(20, 4)));
        Assert.Equal((uint)0, BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.AsSpan(24, 4)));
        Assert.Equal((uint)0, BinaryPrimitives.ReadUInt32LittleEndian(frameBytes.AsSpan(28, 4)));
    }

    [Fact]
    public void TryReadFrame_ThrowsWhenPayloadLengthExceedsMaximum()
    {
        var reader = new LiveV2ProtocolReader();
        var header = CreateRawFrameHeader(
            frameType: (LiveV2FrameType)0xFFFF,
            payloadLength: (uint)(LiveV2ProtocolConstants.MaxPayloadLength + 1),
            sequence: 1);

        reader.Append(header);

        Assert.Throws<FormatException>(() => reader.TryReadFrame(out _));
    }

    [Fact]
    public void TryReadFrame_SkipsUnknownFrameType_AndReturnsNextKnownFrame()
    {
        var reader = new LiveV2ProtocolReader();
        var unknownFrame = CreateRawFrameHeader(
            frameType: (LiveV2FrameType)0xFFFF,
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

        var frame = Assert.IsType<LiveGpsBatchFrame>(LiveV2ProtocolReader.ParseFrame(frameBytes));

        Assert.Equal((uint)88, frame.Batch.SessionId);
        var decoded = Assert.Single(frame.Records);
        Assert.Equal(record.Timestamp, decoded.Timestamp);
        Assert.Equal(record.Latitude, decoded.Latitude);
        Assert.Equal(record.Longitude, decoded.Longitude);
        Assert.Equal(record.FixMode, decoded.FixMode);
        Assert.Equal(record.Satellites, decoded.Satellites);
    }

    private static byte[] CreateRawFrameHeader(LiveV2FrameType frameType, uint payloadLength, uint sequence)
    {
        var header = new byte[LiveV2ProtocolConstants.FrameHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(0, 4), LiveV2ProtocolConstants.Magic);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), LiveV2ProtocolConstants.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), (ushort)frameType);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), payloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), sequence);
        return header;
    }
}

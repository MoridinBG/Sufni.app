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
    public void ParseFrame_ReturnsSessionHeaderFrame_WithCalibrationAndImuLocations()
    {
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel();
        var frameBytes = LiveProtocolTestFrames.CreateSessionHeaderFrame(7, sessionHeader);

        var frame = Assert.IsType<LiveSessionHeaderFrame>(LiveProtocolReader.ParseFrame(frameBytes));

        Assert.Equal(sessionHeader.SessionId, frame.Payload.SessionId);
        Assert.Equal(sessionHeader.ActiveImuMask, frame.Payload.ActiveImuMask);
        Assert.Equal(sessionHeader.ImuCalibrationScales.FrameAccelLsbPerG, frame.Payload.ImuCalibrationScales.FrameAccelLsbPerG);
        Assert.Equal(sessionHeader.ImuCalibrationScales.RearGyroLsbPerDps, frame.Payload.ImuCalibrationScales.RearGyroLsbPerDps);
        Assert.Equal(new[] { LiveImuLocation.Frame, LiveImuLocation.Rear }, frame.Payload.GetActiveImuLocations());
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
        var frameBytes = LiveProtocolTestFrames.CreateGpsBatchFrame(sequence: 9, sessionId: 88, record: record, queueDepth: 2, droppedBatches: 1);

        var frame = Assert.IsType<LiveGpsBatchFrame>(LiveProtocolReader.ParseFrame(frameBytes));

        Assert.Equal((uint)88, frame.Batch.SessionId);
        Assert.Equal((uint)2, frame.Batch.QueueDepth);
        Assert.Equal((uint)1, frame.Batch.DroppedBatches);
        var decoded = Assert.Single(frame.Records);
        Assert.Equal(record.Timestamp, decoded.Timestamp);
        Assert.Equal(record.Latitude, decoded.Latitude);
        Assert.Equal(record.Longitude, decoded.Longitude);
        Assert.Equal(record.FixMode, decoded.FixMode);
        Assert.Equal(record.Satellites, decoded.Satellites);
    }
}
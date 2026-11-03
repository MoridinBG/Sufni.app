using System.Buffers.Binary;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveV3ProtocolSerializationTests
{
    [Fact]
    public void CreateHandshakeAndControlFrames_WritesExpectedLayouts()
    {
        Assert.Equal(
            LiveV3ProtocolTestFrames.Handshake(),
            LiveV3ProtocolReader.CreateHandshake());
        Assert.Equal(
            LiveV3ProtocolTestFrames.CapabilitiesRequest(),
            LiveV3ProtocolReader.CreateCapabilitiesRequestFrame(sequence: 0));
        Assert.Equal(
            LiveV3ProtocolTestFrames.StopRequest(),
            LiveV3ProtocolReader.CreateStopRequestFrame(sessionId: 1, sequence: 4));
    }

    [Fact]
    public void CreateStartRequestFrame_WritesAllSelectedStreams()
    {
        var request = new LiveV3StartRequest
        {
            StartFlags = LiveV3ProtocolConstants.StartFlagPriority |
                         LiveV3ProtocolConstants.StartFlagNoGpsHeaderWait,
            StreamRequests =
            [
                Request(SstV5ProtocolConstants.StreamTravel, LiveSensorInstanceMask.Travel, 200_000, 50),
                Request(
                    SstV5ProtocolConstants.StreamImu,
                    LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
                    200_000,
                    50),
                Request(
                    SstV5ProtocolConstants.StreamTemperature,
                    LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
                    30,
                    batchDurationMs: 0),
                Request(
                    SstV5ProtocolConstants.StreamGps,
                    LiveSensorInstanceMask.Gps,
                    5_000,
                    batchDurationMs: 0,
                    extensionMask: SstV5ProtocolConstants.ExtensionGpsDiagPublicV1),
                new LiveV3StreamRequestRecord(
                    SstV5ProtocolConstants.StreamBattery,
                    RecordFlags: 0,
                    SourceMask: LiveSensorInstanceMask.Battery,
                    ExtensionMask: 0,
                    RateMhz: 0,
                    BatchDurationMs: 0),
                new LiveV3StreamRequestRecord(
                    SstV5ProtocolConstants.StreamMarker,
                    RecordFlags: 0,
                    SourceMask: LiveSensorInstanceMask.None,
                    ExtensionMask: 0,
                    RateMhz: 0,
                    BatchDurationMs: 0),
            ],
        };

        Assert.Equal(
            LiveV3ProtocolTestFrames.StartRequestAllStreams(),
            LiveV3ProtocolReader.CreateStartRequestFrame(sequence: 2, request));
    }

    [Fact]
    public void CreateStartRequestFrame_WritesTemperatureOnlyRequest()
    {
        var request = new LiveV3StartRequest
        {
            StreamRequests =
            [
                new LiveV3StreamRequestRecord(
                    SstV5ProtocolConstants.StreamTemperature,
                    LiveV3ProtocolConstants.StreamRequestFlagRateOverride,
                    LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
                    ExtensionMask: 0,
                    RateMhz: 30,
                    BatchDurationMs: 0),
            ],
        };

        Assert.Equal(
            LiveV3ProtocolTestFrames.StartRequestTemperatureOnly(),
            LiveV3ProtocolReader.CreateStartRequestFrame(sequence: 0, request));
    }

    [Fact]
    public void ParseStartResult_UsesHeaderSessionAndPreservesReasons()
    {
        var context = new LiveV3SessionDecodeContext();
        var pending = Assert.IsType<LiveV3StartResultFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.StartResultPending(),
                context));
        var denied = Assert.IsType<LiveV3StartResultFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.StartResultDenied(),
                new LiveV3SessionDecodeContext()));

        Assert.True(pending.Payload.IsPending);
        Assert.Equal((byte)42, pending.Payload.SessionId);
        Assert.True(denied.Payload.IsDenied);
        Assert.Equal((byte)0, denied.Payload.SessionId);
        Assert.Collection(
            denied.Payload.AdmissionReasons,
            reason =>
            {
                Assert.Equal((byte)1, reason.TargetKind);
                Assert.Equal((byte)0, reason.StreamKind);
                Assert.Equal(LiveStreamMask.Travel | LiveStreamMask.Imu, reason.Stream);
                Assert.Equal((uint)(LiveStreamMask.Travel | LiveStreamMask.Imu), reason.TargetMask);
            },
            reason =>
            {
                Assert.Equal((byte)2, reason.TargetKind);
                Assert.Equal(SstV5ProtocolConstants.StreamImu, reason.StreamKind);
                Assert.Equal(LiveStreamMask.Imu, reason.Stream);
                Assert.Equal(LiveSensorInstanceMask.RearImu, reason.Sources);
                Assert.Equal((uint)LiveSensorInstanceMask.RearImu, reason.TargetMask);
            });
    }

    [Fact]
    public void ParseHelloAndHeader_ReadsSpecifiedOffsets()
    {
        var hello = LiveV3ProtocolReader.ParseServerHello(
            LiveV3ProtocolTestFrames.ServerHello());
        var header = LiveV3ProtocolReader.ParseHeader(
            LiveV3ProtocolTestFrames.StartResultPending());

        Assert.Equal((ulong)0x0123456789abcdef, hello.UniqueBoardId);
        Assert.Equal(0, hello.ProtoMinor);
        Assert.Equal(42, header.SessionId);
        Assert.Equal(LiveV3FrameType.StartResult, header.FrameType);
        Assert.Equal(0, header.FrameFlags);
        Assert.Equal(4u, header.PayloadLength);
    }

    [Fact]
    public void ParseCapabilities_ParsesEveryField()
    {
        var frame = Assert.IsType<LiveV3CapabilitiesFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.CapabilitiesResponse(),
                new LiveV3SessionDecodeContext()));

        Assert.Equal((byte)2, frame.Payload.BoardId);
        Assert.Equal(
            LiveStreamMask.Travel |
            LiveStreamMask.Imu |
            LiveStreamMask.Temperature |
            LiveStreamMask.Gps |
            LiveStreamMask.Battery |
            LiveStreamMask.Marker,
            frame.Payload.SupportedStreamMask);
        Assert.Equal(
            LiveSensorInstanceMask.Travel |
            LiveSensorInstanceMask.FrameImu |
            LiveSensorInstanceMask.ForkImu |
            LiveSensorInstanceMask.Gps |
            LiveSensorInstanceMask.Battery,
            frame.Payload.SupportedSourceMask);
        Assert.Equal(4096u, frame.Payload.MaxFramePayloadBytes);
        Assert.Equal(6, frame.Payload.Streams.Count);

        var travel = frame.Payload.Streams[0];
        Assert.Equal(LiveStreamMask.Travel, travel.Stream);
        Assert.Equal(SstV5ProtocolConstants.TimingFixedRate, travel.TimingModelId);
        Assert.Equal(1_000u, travel.MinRateMhz);
        Assert.Equal(1_000_000u, travel.MaxRateMhz);
        Assert.Equal(10_000u, travel.MaxBatchDurationMs);

        var gps = frame.Payload.Streams[3];
        Assert.Equal(LiveStreamMask.Gps, gps.Stream);
        Assert.Equal(SstV5ProtocolConstants.TimingGpsReceiverTimed, gps.TimingModelId);
        Assert.Equal(SstV5ProtocolConstants.ExtensionGpsDiagPublicV1, gps.SupportedExtensionMask);
    }

    [Fact]
    public void ParseSessionHeader_ParsesDirectDescriptorsAndOmission()
    {
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(42);

        var frame = Assert.IsType<LiveSessionHeaderFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.SessionHeaderAllStreams(),
                context));

        Assert.Equal((byte)2, frame.Payload.BoardId);
        Assert.Equal(LiveSensorInstanceMask.None, frame.Payload.RequestedSensorMask);
        Assert.Equal(
            LiveStreamMask.Travel |
            LiveStreamMask.Imu |
            LiveStreamMask.Temperature |
            LiveStreamMask.Gps |
            LiveStreamMask.Battery |
            LiveStreamMask.Marker,
            frame.Payload.AcceptedStreamMask);
        Assert.Equal(
            LiveSensorInstanceMask.Travel |
            LiveSensorInstanceMask.FrameImu |
            LiveSensorInstanceMask.ForkImu |
            LiveSensorInstanceMask.Gps |
            LiveSensorInstanceMask.Battery,
            frame.Payload.AcceptedSensorMask);
        Assert.Equal(200_000u, frame.Payload.AcceptedTravelRateMhz);
        Assert.Equal(200_000u, frame.Payload.AcceptedImuRateMhz);
        Assert.Equal(30u, frame.Payload.AcceptedTemperatureRateMhz);
        Assert.Equal(5_000u, frame.Payload.AcceptedGpsRateMhz);
        Assert.Equal(LiveImuLocationMask.Frame | LiveImuLocationMask.Fork, frame.Payload.ActiveImuMask);
        var omission = Assert.Single(frame.Payload.AdmissionOmissions);
        Assert.Equal((byte)3, omission.TargetKind);
        Assert.Equal(SstV5ProtocolConstants.StreamGps, omission.StreamKind);
        Assert.Equal(SstV5ProtocolConstants.ExtensionGpsDiagPublicV1, omission.TargetMask);
    }

    [Theory]
    [InlineData(13, 5)]
    [InlineData(16, 6)]
    [InlineData(60, 1)]
    [InlineData(39, 1)]
    [InlineData(56, 3)]
    [InlineData(236, 1)]
    [InlineData(-1, 0)]
    public void ParseSessionHeader_RejectsMalformedDescriptor(int offset, byte value)
    {
        var bytes = LiveV3ProtocolTestFrames.SessionHeaderAllStreams();
        if (offset < 0) bytes = bytes[..^1];
        else bytes[offset] = value;
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(42);
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(bytes, context));
    }

    [Theory]
    [InlineData("travel")]
    [InlineData("imu")]
    [InlineData("temperature")]
    [InlineData("gps")]
    [InlineData("battery")]
    [InlineData("marker")]
    [InlineData("status")]
    [InlineData("terminal")]
    [InlineData("pong")]
    [InlineData("error")]
    public void ParseFrame_DecodesCanonicalWireValue(string kind)
    {
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(42);
        _ = Parse(LiveV3ProtocolTestFrames.SessionHeaderAllStreams(), context);
        var bytes = kind switch
        {
            "travel" => LiveV3ProtocolTestFrames.TravelData(),
            "imu" => LiveV3ProtocolTestFrames.ImuData(),
            "temperature" => LiveV3ProtocolTestFrames.TemperatureData(),
            "gps" => LiveV3ProtocolTestFrames.GpsData(),
            "battery" => LiveV3ProtocolTestFrames.BatteryData(),
            "marker" => LiveV3ProtocolTestFrames.MarkerData(),
            "status" => LiveV3ProtocolTestFrames.StatusAllStreams(),
            "terminal" => LiveV3ProtocolTestFrames.SessionResultAllStreams(),
            "pong" => LiveV3ProtocolTestFrames.Pong(),
            _ => LiveV3ProtocolTestFrames.Error(),
        };
        var frame = Parse(bytes, context);
        switch (kind)
        {
            case "travel":
                Assert.Equal(new LiveTravelRecord(1000, 1111), Assert.Single(Assert.IsType<LiveTravelBatchFrame>(frame).Records));
                break;
            case "imu":
                Assert.Equal(new short[] { 100, 700 }, Assert.IsType<LiveImuBatchFrame>(frame).Records.Select(record => record.Ax));
                break;
            case "temperature":
                Assert.Collection(Assert.IsType<LiveTemperatureBatchFrame>(frame).Records,
                    record => { Assert.Equal(LiveSensorInstanceMask.FrameImu, record.Source); Assert.Equal(37.53f, record.Sample.TemperatureCelsius, 2); },
                    record => { Assert.Equal(LiveSensorInstanceMask.ForkImu, record.Source); Assert.Equal(24f, record.Sample.TemperatureCelsius, 2); });
                break;
            case "gps":
                var fix = Assert.Single(Assert.IsType<LiveGpsBatchFrame>(frame).Records);
                Assert.Equal(42.6975123, fix.Latitude, 7);
                Assert.Equal(23.3216789, fix.Longitude, 7);
                break;
            case "battery":
                Assert.Equal((ushort)3987, Assert.Single(Assert.IsType<LiveBatteryBatchFrame>(frame).Records).Millivolts);
                break;
            case "marker":
                Assert.Equal((byte)1, Assert.Single(Assert.IsType<LiveMarkerBatchFrame>(frame).Records).MarkerType);
                break;
            case "status":
                var status = Assert.Single(Assert.IsType<LiveStatusFrame>(frame).Streams, item => item.Stream == LiveStreamMask.Travel);
                Assert.Equal((ulong)11, status.ProducerMissedCount);
                Assert.Equal((ushort)1, status.SinkBacklogBatches);
                break;
            case "terminal":
                Assert.Equal((ushort)6, Assert.IsType<LiveSessionResultFrame>(frame).Payload.FinalStatus.Streams[5].SinkBacklogBatches);
                break;
            case "pong":
                Assert.Equal(0xa1b2c3d4u, Assert.IsType<LivePongFrame>(frame).Nonce);
                break;
            case "error":
                Assert.Equal(0xdecafbadu, Assert.IsType<LiveV3ErrorFrame>(frame).Payload.Detail);
                break;
        }
    }

    [Theory]
    [InlineData(13, 5)]
    [InlineData(52, 1)]
    [InlineData(30, 1)]
    [InlineData(16, 128)]
    [InlineData(32, 128)]
    public void ParseCapabilities_RejectsMalformedDescriptor(int offset, byte value)
    {
        var bytes = LiveV3ProtocolTestFrames.CapabilitiesResponse();
        bytes[offset] = value;
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(bytes, new LiveV3SessionDecodeContext()));
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData(byte.MaxValue)]
    public void ParseServerHello_AcceptsAnyProtocolMinor(byte protocolMinor)
    {
        var hello = LiveV3ProtocolTestFrames.ServerHello();
        hello[5] = protocolMinor;

        Assert.Equal(protocolMinor, LiveV3ProtocolReader.ParseServerHello(hello).ProtoMinor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseFrame_RejectsServerControlFromWrongSession(bool error)
    {
        var bytes = error ? LiveV3ProtocolTestFrames.Error() : LiveV3ProtocolTestFrames.CapabilitiesResponse();
        bytes[0] = 41;
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(42);
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(bytes, context));
    }

    [Theory]
    [InlineData(true, 18, 0)]
    [InlineData(true, 18, 9)]
    [InlineData(false, 12, 1)]
    [InlineData(false, 13, 1)]
    public void ParseResult_RejectsInvalidReasonOrReservedValue(bool start, int offset, byte value)
    {
        var bytes = start ? LiveV3ProtocolTestFrames.StartResultDenied() : LiveV3ProtocolTestFrames.StopResult();
        bytes[offset] = value;
        var context = new LiveV3SessionDecodeContext();
        if (!start) context.AcceptSession(42);
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(bytes, context));
    }

    private static LiveV3StreamRequestRecord Request(
        byte streamKind,
        LiveSensorInstanceMask sourceMask,
        uint rateMhz,
        uint batchDurationMs,
        uint extensionMask = 0)
    {
        var flags = LiveV3ProtocolConstants.StreamRequestFlagRateOverride;
        if (batchDurationMs > 0)
        {
            flags |= LiveV3ProtocolConstants.StreamRequestFlagBatchDurationOverride;
        }

        return new LiveV3StreamRequestRecord(
            streamKind,
            flags,
            sourceMask,
            ExtensionMask: extensionMask,
            RateMhz: rateMhz,
            BatchDurationMs: batchDurationMs);
    }

    private static LiveProtocolFrame Parse(byte[] frame, LiveV3SessionDecodeContext context) =>
        LiveV3ProtocolReader.ParseFrame(frame, context);
}

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
            LiveV3ProtocolTestFrames.DeviceStateRequest(),
            LiveV3ProtocolReader.CreateDeviceStateRequestFrame(sequence: 1));
        Assert.Equal(
            LiveV3ProtocolTestFrames.StopRequest(),
            LiveV3ProtocolReader.CreateStopRequestFrame(sessionId: 1, sequence: 4));
        Assert.Equal(
            LiveV3ProtocolTestFrames.Ping(),
            LiveV3ProtocolReader.CreatePingFrame(sessionId: 1, sequence: 3, nonce: 0xa1b2c3d4));
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
        Assert.Equal((byte)42, context.AcceptedSessionId);
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
    public void ParseStartRequest_RejectsDuplicateAndOutOfOrderRecords()
    {
        var frame = LiveV3ProtocolTestFrames.StartRequestAllStreams();

        AssertInvalid(frame, bytes => bytes[36] = SstV5ProtocolConstants.StreamTravel);
        AssertInvalid(frame, bytes => bytes[36] = SstV5ProtocolConstants.StreamMarker);
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
    public void ParseDeviceState_ParsesOrderedAdvisoryState()
    {
        var frame = Assert.IsType<LiveV3DeviceStateFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolTestFrames.DeviceStateResponse(),
                new LiveV3SessionDecodeContext()));

        Assert.Equal(6, frame.Payload.Streams.Count);
        Assert.Equal(6, frame.Payload.Sources.Count);
        Assert.Equal(SstV5ProtocolConstants.StreamTravel, frame.Payload.Streams[0].StreamKind);
        Assert.Equal(200_000u, frame.Payload.Streams[0].EffectiveDefaultRateMhz);
        Assert.Equal(50u, frame.Payload.Streams[0].DefaultBatchDurationMs);
        Assert.Equal(LiveSensorInstanceMask.ForkTravel, frame.Payload.Sources[0].Source);
        Assert.False(
            Assert.Single(
                frame.Payload.Sources,
                source => source.Source == LiveSensorInstanceMask.ForkImu).Available);
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
        Assert.Equal(6, frame.Payload.StreamDescriptors.Count);
        Assert.Collection(
            frame.Payload.StreamDescriptors,
            stream => Assert.Equal((ushort)4, stream.CompactPayloadRecordBytes),
            stream => Assert.Equal((ushort)24, stream.CompactPayloadRecordBytes),
            stream => Assert.Equal((ushort)4, stream.CompactPayloadRecordBytes),
            stream => Assert.Equal((ushort)30, stream.CompactPayloadRecordBytes),
            stream => Assert.Equal((ushort)4, stream.CompactPayloadRecordBytes),
            stream => Assert.Equal((ushort)1, stream.CompactPayloadRecordBytes));
        var omission = Assert.Single(frame.Payload.AdmissionOmissions);
        Assert.Equal((byte)3, omission.TargetKind);
        Assert.Equal(SstV5ProtocolConstants.StreamGps, omission.StreamKind);
        Assert.Equal(SstV5ProtocolConstants.ExtensionGpsDiagPublicV1, omission.TargetMask);
        Assert.True(context.HasSessionHeader);
    }

    [Fact]
    public void ParseSessionHeader_RejectsCountOrderMaskSizeGroupingAndLengthMismatches()
    {
        var frame = LiveV3ProtocolTestFrames.SessionHeaderAllStreams();

        AssertInvalidStarted(frame, bytes => bytes[13] = 5);
        AssertInvalidStarted(
            frame,
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(16, 4),
                (uint)(LiveStreamMask.Travel | LiveStreamMask.Imu)));
        AssertInvalidStarted(frame, bytes => bytes[60] = SstV5ProtocolConstants.StreamTravel);
        AssertInvalidStarted(frame, bytes => bytes[39] = 1);
        AssertInvalidStarted(frame, bytes => bytes[56] = 3);
        AssertInvalidStarted(frame, bytes => bytes[236] = SstV5ProtocolConstants.StreamTravel);

        var truncated = frame[..^1];
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(42);
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(truncated, context));
    }

    [Fact]
    public void ParseDataStatusTerminalPongAndError_ReturnsCanonicalFrames()
    {
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(42);
        _ = LiveV3ProtocolReader.ParseFrame(
            LiveV3ProtocolTestFrames.SessionHeaderAllStreams(),
            context);

        var travel = Assert.IsType<LiveTravelBatchFrame>(Parse(LiveV3ProtocolTestFrames.TravelData(), context));
        Assert.Equal((ulong)1001, travel.Batch.FirstIndex);
        Assert.Equal(new LiveTravelRecord(1000, 1111), Assert.Single(travel.Records));

        var imu = Assert.IsType<LiveImuBatchFrame>(Parse(LiveV3ProtocolTestFrames.ImuData(), context));
        Assert.Collection(
            imu.Records,
            record => Assert.Equal((short)100, record.Ax),
            record => Assert.Equal((short)700, record.Ax));

        var temperature = Assert.IsType<LiveTemperatureBatchFrame>(
            Parse(LiveV3ProtocolTestFrames.TemperatureData(), context));
        Assert.Collection(
            temperature.Records,
            record =>
            {
                Assert.Equal(LiveSensorInstanceMask.FrameImu, record.Source);
                Assert.Equal(37.53f, record.Sample.TemperatureCelsius, 2);
            },
            record =>
            {
                Assert.Equal(LiveSensorInstanceMask.ForkImu, record.Source);
                Assert.Equal(24f, record.Sample.TemperatureCelsius, 2);
            });

        var gps = Assert.IsType<LiveGpsBatchFrame>(Parse(LiveV3ProtocolTestFrames.GpsData(), context));
        var gpsRecord = Assert.Single(gps.Records);
        Assert.Equal(42.6975123, gpsRecord.Latitude, 7);
        Assert.Equal(23.3216789, gpsRecord.Longitude, 7);
        Assert.Equal(14, gpsRecord.Satellites);

        var battery = Assert.IsType<LiveBatteryBatchFrame>(Parse(LiveV3ProtocolTestFrames.BatteryData(), context));
        Assert.Equal((ushort)3987, Assert.Single(battery.Records).Millivolts);
        var marker = Assert.IsType<LiveMarkerBatchFrame>(Parse(LiveV3ProtocolTestFrames.MarkerData(), context));
        Assert.Equal(SstV5ProtocolConstants.MarkerManualUserMark, Assert.Single(marker.Records).MarkerType);

        var status = Assert.IsType<LiveStatusFrame>(Parse(LiveV3ProtocolTestFrames.StatusAllStreams(), context));
        Assert.Equal(6, status.Streams.Count);
        Assert.Equal(LiveStreamMask.Travel, status.Streams[0].Stream);
        Assert.Equal((byte)1, status.Streams[0].ProducerState);
        Assert.Equal((byte)0, status.Streams[0].ProducerFailureReason);
        Assert.Equal((ushort)1, status.Streams[0].SinkBacklogBatches);
        Assert.Equal((ulong)11, status.Streams[0].ProducerMissedCount);
        Assert.Equal(LiveStreamMask.Marker, status.Streams[5].Stream);
        Assert.Equal((byte)0, status.Streams[5].ProducerState);

        var terminal = Assert.IsType<LiveSessionResultFrame>(
            Parse(LiveV3ProtocolTestFrames.SessionResultAllStreams(), context));
        Assert.Equal(6, terminal.Payload.FinalStatus.Streams.Length);
        Assert.Equal((ushort)6, terminal.Payload.FinalStatus.Streams[5].SinkBacklogBatches);

        var pong = Assert.IsType<LivePongFrame>(Parse(LiveV3ProtocolTestFrames.Pong(), context));
        Assert.Equal(0xa1b2c3d4u, pong.Nonce);
        var error = Assert.IsType<LiveV3ErrorFrame>(Parse(LiveV3ProtocolTestFrames.Error(), context));
        Assert.Equal((byte)2, error.Payload.Code);
        Assert.Equal((byte)99, error.Payload.OffendingFrameType);
        Assert.Equal(0xdecafbadu, error.Payload.Detail);
    }

    [Fact]
    public void ParsePreheaderTerminalAndPing_DoesNotRequireDescriptorsOrSequenceContinuity()
    {
        var preheaderContext = new LiveV3SessionDecodeContext();
        preheaderContext.AcceptSession(43);
        var terminal = Assert.IsType<LiveSessionResultFrame>(
            Parse(LiveV3ProtocolTestFrames.SessionResultPreheaderFailure(), preheaderContext));
        Assert.Empty(terminal.Payload.FinalStatus.Streams);

        var ping = Assert.IsType<LivePingFrame>(
            Parse(LiveV3ProtocolTestFrames.Ping(), new LiveV3SessionDecodeContext()));
        Assert.Equal(0xa1b2c3d4u, ping.Nonce);

        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(42);
        _ = Parse(LiveV3ProtocolTestFrames.SessionHeaderAllStreams(), context);
        var discontinuous = LiveV3ProtocolTestFrames.TravelData();
        BinaryPrimitives.WriteUInt32LittleEndian(discontinuous.AsSpan(8, 4), uint.MaxValue);
        var travel = Assert.IsType<LiveTravelBatchFrame>(
            LiveV3ProtocolReader.ParseFrame(discontinuous, context));
        Assert.Equal(uint.MaxValue, travel.Sequence);
    }

    [Fact]
    public void ParseCapabilities_RejectsMalformedCountsOrderingReservedAndMasks()
    {
        var frame = LiveV3ProtocolTestFrames.CapabilitiesResponse();

        AssertInvalid(frame, bytes => bytes[13] = 5);
        AssertInvalid(frame, bytes => bytes[52] = SstV5ProtocolConstants.StreamTravel);
        AssertInvalid(frame, bytes => bytes[30] = 1);
        AssertInvalid(
            frame,
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16, 4), 0x80));
        AssertInvalid(
            frame,
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32, 4), 0x80));
    }

    [Fact]
    public void ParseDeviceState_RejectsMalformedCountsOrderingReservedAndMasks()
    {
        var frame = LiveV3ProtocolTestFrames.DeviceStateResponse();

        AssertInvalid(frame, bytes => bytes[12] = 5);
        AssertInvalid(frame, bytes => bytes[28] = SstV5ProtocolConstants.StreamTravel);
        AssertInvalid(frame, bytes => bytes[17] = 1);
        AssertInvalid(
            frame,
            bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(92, 4), 0x80));
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

    [Fact]
    public void ParseHeader_AllowsUnknownServerTypeOnlyWhenRequested()
    {
        var bytes = LiveV3ProtocolTestFrames.Pong();
        bytes[1] = 0xee;

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseHeader(bytes));
        var header = LiveV3ProtocolReader.ParseHeader(bytes, allowUnknownFrameType: true);
        Assert.Equal((byte)0xee, (byte)header.FrameType);
        Assert.Equal(42, header.SessionId);
        Assert.Equal(4u, header.PayloadLength);
    }

    [Fact]
    public void ControlFrameSessionScopes_AreStrict()
    {
        AssertInvalid(
            LiveV3ProtocolTestFrames.CapabilitiesRequest(),
            bytes => bytes[0] = 42);
        AssertInvalid(
            LiveV3ProtocolTestFrames.CapabilitiesResponse(),
            bytes => bytes[0] = 42);
        AssertInvalid(
            LiveV3ProtocolTestFrames.StartRequestAllStreams(),
            bytes => bytes[0] = 42);
        AssertInvalid(
            LiveV3ProtocolTestFrames.StopRequest(),
            bytes => bytes[0] = 0);

        var error = LiveV3ProtocolTestFrames.Error();
        error[0] = 41;
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(42);
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(error, context));
    }

    [Fact]
    public void StartAndStopResults_RejectUndefinedReasonAndReservedValues()
    {
        AssertInvalid(
            LiveV3ProtocolTestFrames.StartResultDenied(),
            bytes => bytes[18] = 0);
        AssertInvalid(
            LiveV3ProtocolTestFrames.StartResultDenied(),
            bytes => bytes[18] = 9);

        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(42);
        _ = LiveV3ProtocolReader.ParseFrame(
            LiveV3ProtocolTestFrames.SessionHeaderAllStreams(),
            context);
        var invalidCode = LiveV3ProtocolTestFrames.StopResult();
        invalidCode[12] = 1;
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(invalidCode, context));
        var invalidReserved = LiveV3ProtocolTestFrames.StopResult();
        invalidReserved[13] = 1;
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(invalidReserved, context));
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

    private static void AssertInvalid(byte[] frame, Action<byte[]> mutate)
    {
        var malformed = (byte[])frame.Clone();
        mutate(malformed);
        Assert.Throws<FormatException>(
            () => LiveV3ProtocolReader.ParseFrame(malformed, new LiveV3SessionDecodeContext()));
    }

    private static void AssertInvalidStarted(byte[] frame, Action<byte[]> mutate)
    {
        var malformed = (byte[])frame.Clone();
        mutate(malformed);
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(42);
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(malformed, context));
    }

    private static LiveProtocolFrame Parse(byte[] frame, LiveV3SessionDecodeContext context) =>
        LiveV3ProtocolReader.ParseFrame(frame, context);
}

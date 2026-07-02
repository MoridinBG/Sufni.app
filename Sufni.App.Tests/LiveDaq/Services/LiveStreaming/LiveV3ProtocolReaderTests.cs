using System.Buffers.Binary;
using System.Text;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;
using static Sufni.App.Tests.LiveDaq.Services.LiveStreaming.LiveV3ProtocolTestFrames;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveV3ProtocolReaderTests
{
    [Fact]
    public void CreateHandshake_WritesMagicAndZeroFlagsReserved()
    {
        var handshake = LiveV3ProtocolReader.CreateHandshake();

        Assert.Equal(LiveV3ProtocolConstants.HandshakeSize, handshake.Length);
        Assert.Equal("LIV3", Encoding.ASCII.GetString(handshake, 0, 4));
        Assert.Equal([0, 0, 0], handshake[4..]);
    }

    [Fact]
    public void ParseServerHello_ParsesMinorFeaturesBoardAndFirmware()
    {
        var helloBytes = LiveV3ProtocolReader.CreateServerHello(
            uniqueBoardId: 0x0102030405060708,
            protoMinor: 2,
            featureFlags: 0x8001,
            firmwareVersionMajor: 1,
            firmwareVersionMinor: 2,
            firmwareVersionPatch: 3);

        var hello = LiveV3ProtocolReader.ParseServerHello(helloBytes);

        Assert.Equal((byte)2, hello.ProtoMinor);
        Assert.Equal((ushort)0x8001, hello.FeatureFlags);
        Assert.Equal((uint)LiveV3ProtocolConstants.MaxPayloadLength, hello.MaxFramePayloadBytes);
        Assert.Equal((ulong)0x0102030405060708, hello.UniqueBoardId);
        Assert.Equal(new Version(1, 2, 3), hello.FirmwareVersion);
    }

    [Fact]
    public void ParseServerHello_RejectsInvalidMagicMajorAndZeroBoardId()
    {
        var invalidMagic = LiveV3ProtocolReader.CreateServerHello(1);
        invalidMagic[0] = (byte)'X';
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseServerHello(invalidMagic));

        var invalidMajor = LiveV3ProtocolReader.CreateServerHello(1);
        invalidMajor[4] = 4;
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseServerHello(invalidMajor));

        var zeroBoard = LiveV3ProtocolReader.CreateServerHello(1);
        Array.Clear(zeroBoard, 12, 8);
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseServerHello(zeroBoard));

        var longHello = new byte[LiveV3ProtocolConstants.ServerHelloSize + 1];
        LiveV3ProtocolReader.CreateServerHello(1).CopyTo(longHello, 0);
        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseServerHello(longHello));
    }

    [Fact]
    public void ParseHeader_Parses12ByteLayout()
    {
        var frameBytes = LiveV3ProtocolReader.CreateFrame(
            LiveV3FrameType.Pong,
            SessionId,
            sequence: 42,
            [0xAA, 0xBB]);

        var header = LiveV3ProtocolReader.ParseHeader(frameBytes);

        Assert.Equal(LiveV3FrameType.Pong, header.FrameType);
        Assert.Equal(0, header.Flags);
        Assert.Equal(SessionId, header.SessionId);
        Assert.Equal(0, header.Reserved);
        Assert.Equal((uint)2, header.PayloadLength);
        Assert.Equal((uint)42, header.TxSequence);
        Assert.Equal(frameBytes.Length, header.TotalFrameLength);
    }

    [Fact]
    public void ParseHeader_RejectsUnknownFrameType()
    {
        var header = CreateRawHeader(rawFrameType: 0xFF, payloadLength: 0, sequence: 1);

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseHeader(header));
    }

    [Fact]
    public void ParseHeader_AllowsUnknownFrameType_WhenRequested()
    {
        var headerBytes = CreateRawHeader(rawFrameType: 0xFE, payloadLength: 4, sequence: 9);

        var header = LiveV3ProtocolReader.ParseHeader(headerBytes, allowUnknownFrameType: true);

        Assert.Equal((LiveV3FrameType)0xFE, header.FrameType);
        Assert.Equal((uint)4, header.PayloadLength);
        Assert.Equal((uint)9, header.TxSequence);
    }

    [Fact]
    public void ParseHeader_RejectsNonzeroFlags()
    {
        var header = CreateRawHeader(LiveV3FrameType.Pong, payloadLength: 0, sequence: 1);
        header[1] = 1;

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseHeader(header));
    }

    [Fact]
    public void ParseHeader_RejectsPayloadLengthOverMaximum()
    {
        var header = CreateRawHeader(
            LiveV3FrameType.Pong,
            payloadLength: LiveV3ProtocolConstants.MaxPayloadLength + 1,
            sequence: 1);

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseHeader(header));
    }

    [Fact]
    public void CreateStartRequestFrame_WritesRecordBasedPayload()
    {
        var request = new LiveV3StartRequest
        {
            StartFlags = LiveV3ProtocolConstants.StartFlagPriority | LiveV3ProtocolConstants.StartFlagNoGpsHeaderWait,
            StreamRequests =
            [
                new LiveV3StreamRequestRecord(
                    SstV5ProtocolConstants.StreamTravel,
                    LiveV3ProtocolConstants.StreamRequestFlagRateOverride | LiveV3ProtocolConstants.StreamRequestFlagBatchDurationOverride,
                    LiveSensorInstanceMask.Travel,
                    ExtensionMask: 0,
                    RateMhz: 200_000,
                    BatchDurationMs: 50),
                new LiveV3StreamRequestRecord(
                    SstV5ProtocolConstants.StreamImu,
                    LiveV3ProtocolConstants.StreamRequestFlagRateOverride | LiveV3ProtocolConstants.StreamRequestFlagBatchDurationOverride,
                    LiveSensorInstanceMask.Imu,
                    ExtensionMask: 0,
                    RateMhz: 100_000,
                    BatchDurationMs: 50),
                new LiveV3StreamRequestRecord(
                    SstV5ProtocolConstants.StreamGps,
                    LiveV3ProtocolConstants.StreamRequestFlagRateOverride,
                    LiveSensorInstanceMask.Gps,
                    SstV5ProtocolConstants.ExtensionGpsDiagPublicV1,
                    RateMhz: 5_000,
                    BatchDurationMs: 0),
            ],
        };

        var frameBytes = LiveV3ProtocolReader.CreateStartRequestFrame(sequence: 12, request);
        var header = LiveV3ProtocolReader.ParseHeader(frameBytes);
        var payload = frameBytes.AsSpan(LiveV3ProtocolConstants.FrameHeaderSize);

        Assert.Equal(LiveV3FrameType.StartReq, header.FrameType);
        Assert.Equal((uint)(LiveV3ProtocolConstants.StartRequestHeaderSize + 3 * LiveV3ProtocolConstants.StreamRequestRecordSize), header.PayloadLength);
        Assert.Equal(3, payload[0]);
        Assert.Equal(0, payload[1]);
        Assert.Equal(LiveV3ProtocolConstants.StartFlagPriority | LiveV3ProtocolConstants.StartFlagNoGpsHeaderWait, ReadUInt16(payload, 2));

        Assert.Equal(SstV5ProtocolConstants.StreamTravel, payload[4]);
        Assert.Equal(LiveV3ProtocolConstants.StreamRequestFlagRateOverride | LiveV3ProtocolConstants.StreamRequestFlagBatchDurationOverride, ReadUInt16(payload, 6));
        Assert.Equal((uint)LiveSensorInstanceMask.Travel, ReadUInt32(payload, 8));
        Assert.Equal((uint)200_000, ReadUInt32(payload, 16));
        Assert.Equal((uint)50, ReadUInt32(payload, 20));

        Assert.Equal(SstV5ProtocolConstants.StreamGps, payload[44]);
        Assert.Equal(LiveV3ProtocolConstants.StreamRequestFlagRateOverride, ReadUInt16(payload, 46));
        Assert.Equal((uint)LiveSensorInstanceMask.Gps, ReadUInt32(payload, 48));
        Assert.Equal(SstV5ProtocolConstants.ExtensionGpsDiagPublicV1, ReadUInt32(payload, 52));
        Assert.Equal((uint)5_000, ReadUInt32(payload, 56));
        Assert.Equal((uint)0, ReadUInt32(payload, 60));

        var parsed = Assert.IsType<LiveV3StartRequestFrame>(
            LiveV3ProtocolReader.ParseFrame(frameBytes, new LiveV3SessionDecodeContext()));
        Assert.Equal(request.StartFlags, parsed.Payload.StartFlags);
        Assert.Equal(3, parsed.Payload.StreamRequests.Count);
    }

    [Fact]
    public void ParseFrame_CapabilitiesResponse_ParsesStreamRecords()
    {
        var payload = CreateCapabilitiesPayload(
            LiveStreamMask.Travel | LiveStreamMask.Imu,
            LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu,
            (SstV5ProtocolConstants.StreamTravel, LiveSensorInstanceMask.Travel, 100_000u, 500_000u));
        var frameBytes = LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.CapabilitiesResp, 0, 3, payload);

        var frame = Assert.IsType<LiveV3CapabilitiesFrame>(
            LiveV3ProtocolReader.ParseFrame(frameBytes, new LiveV3SessionDecodeContext()));

        Assert.Equal((uint)3, frame.Sequence);
        Assert.Equal((uint)LiveV3ProtocolConstants.MaxPayloadLength, frame.Payload.MaxFramePayloadBytes);
        Assert.Equal(LiveStreamMask.Travel | LiveStreamMask.Imu, frame.Payload.SupportedStreamMask);
        var stream = Assert.Single(frame.Payload.Streams);
        Assert.Equal(LiveStreamMask.Travel, stream.Stream);
        Assert.Equal(LiveSensorInstanceMask.Travel, stream.SupportedSourceMask);
        Assert.Equal((uint)100_000, stream.MinRateMhz);
        Assert.Equal((uint)500_000, stream.MaxRateMhz);
    }

    [Fact]
    public void ParseFrame_StartResult_PendingStoresSessionId()
    {
        var payload = new byte[LiveV3ProtocolConstants.StartResultHeaderSize];
        payload[1] = SessionId;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), (uint)LiveStreamMask.Travel);
        var context = new LiveV3SessionDecodeContext();

        var frame = Assert.IsType<LiveV3StartResultFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.StartResult, 0, 4, payload),
                context));

        Assert.True(frame.Payload.IsPending);
        Assert.Equal(SessionId, frame.Payload.SessionId);
        Assert.Equal(SessionId, context.AcceptedSessionId);
    }

    [Fact]
    public void ParseFrame_StartResult_DeniedParsesAdmissionReasons()
    {
        var payload = new byte[LiveV3ProtocolConstants.StartResultHeaderSize + LiveV3ProtocolConstants.AdmissionReasonRecordSize];
        payload[0] = 1;
        payload[2] = 1;
        payload[8] = 2;
        payload[9] = SstV5ProtocolConstants.StreamImu;
        payload[10] = 9;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(12, 4), (uint)LiveSensorInstanceMask.Imu);

        var frame = Assert.IsType<LiveV3StartResultFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.StartResult, 0, 5, payload),
                new LiveV3SessionDecodeContext()));

        Assert.True(frame.Payload.IsDenied);
        var reason = Assert.Single(frame.Payload.AdmissionReasons);
        Assert.Equal((byte)9, reason.Reason);
        Assert.Equal(LiveStreamMask.Imu, reason.Stream);
        Assert.Equal(LiveSensorInstanceMask.Imu, reason.Sources);
        Assert.Equal((byte)2, reason.TargetKind);
        Assert.Equal((uint)LiveSensorInstanceMask.Imu, reason.TargetMask);
    }

    [Fact]
    public void ParseFrame_SessionHeader_ParsesDescriptorsIntoCanonicalHeaderAndContext()
    {
        var context = CreateStartedContext();
        var frame = ParseSessionHeader(
            context,
            TravelStream(),
            ImuStream(),
            GpsStream(),
            MarkerStream());

        Assert.Equal(SessionId, frame.Payload.SessionId);
        Assert.Equal(LiveProtocolVersion.V3, frame.Payload.ProtocolVersion);
        Assert.Equal((uint)200_000, frame.Payload.AcceptedTravelRateMhz);
        Assert.Equal((uint)100_000, frame.Payload.AcceptedImuRateMhz);
        Assert.Equal((uint)5_000, frame.Payload.AcceptedGpsRateMhz);
        Assert.Equal(LiveImuLocationMask.Frame | LiveImuLocationMask.Fork, frame.Payload.ActiveImuMask);
        Assert.Equal(LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu | LiveSensorInstanceMask.Gps, frame.Payload.RequestedSensorMask);
        Assert.Equal(LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu | LiveSensorInstanceMask.Gps, frame.Payload.AcceptedSensorMask);
        Assert.Equal(4, context.StreamDescriptors.Count);
        Assert.True(context.TryGetStream(SstV5ProtocolConstants.StreamTravel, out _));
    }

    [Fact]
    public void ParseFrame_TravelData_UsesSstV5Helpers()
    {
        var context = CreateReadyContext(TravelStream());
        var frameBytes = LiveV3ProtocolReader.CreateFrame(
            LiveV3FrameType.TravelData,
            SessionId,
            20,
            CreateTravelDataPayload(10, 1_000, (100, 200), (101, 201)));

        var frame = Assert.IsType<LiveTravelBatchFrame>(LiveV3ProtocolReader.ParseFrame(frameBytes, context));

        Assert.Equal((uint)20, frame.Sequence);
        Assert.Equal(LiveStreamMask.Travel, frame.Batch.Stream);
        Assert.Equal((ulong)10, frame.Batch.FirstIndex);
        Assert.Equal((ulong)1_000, frame.Batch.FirstMonotonicDeltaUs);
        Assert.Equal(SessionStartMonotonicUs + 1_000, frame.Batch.FirstMonotonicUs);
        Assert.Equal(LiveSensorInstanceMask.Travel, frame.Batch.ValidityMask);
        Assert.Equal([(ushort)100, (ushort)101], frame.Records.Select(record => record.ForkAngle).ToArray());
        Assert.Equal([(ushort)200, (ushort)201], frame.Records.Select(record => record.ShockAngle).ToArray());
    }

    [Fact]
    public void ParseFrame_ImuGpsBatteryAndMarkerData_UseSstV5Helpers()
    {
        var context = CreateReadyContext(ImuStream(), GpsStream(), BatteryStream(), MarkerStream());

        var imuFrame = Assert.IsType<LiveImuBatchFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolReader.CreateFrame(
                    LiveV3FrameType.ImuData,
                    SessionId,
                    21,
                    CreateImuDataPayload(
                        0,
                        0,
                        (new ImuRecordSpec(1, 2, 3, 4, 5, 6), new ImuRecordSpec(7, 8, 9, 10, 11, 12)))),
                context));
        Assert.Equal(2, imuFrame.Records.Count);
        Assert.Equal((short)1, imuFrame.Records[0].Ax);
        Assert.Equal((short)7, imuFrame.Records[1].Ax);

        var gpsFrame = Assert.IsType<LiveGpsBatchFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolReader.CreateFrame(
                    LiveV3FrameType.GpsData,
                    SessionId,
                    22,
                    CreateGpsDataPayload(0, 0)),
                context));
        var gps = Assert.Single(gpsFrame.Records);
        Assert.Equal(48.2082, gps.Latitude, precision: 6);
        Assert.Equal(16.3738, gps.Longitude, precision: 6);

        var batteryFrame = Assert.IsType<LiveBatteryBatchFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolReader.CreateFrame(
                    LiveV3FrameType.BatteryData,
                    SessionId,
                    23,
                    CreateBatteryDataPayload(1, 1_500, 4200, 0x0003)),
                context));
        var battery = Assert.Single(batteryFrame.Records);
        Assert.Equal((ushort)4200, battery.Millivolts);
        Assert.Equal((ushort)0x0003, battery.Flags);

        var markerFrame = Assert.IsType<LiveMarkerBatchFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolReader.CreateFrame(
                    LiveV3FrameType.MarkerData,
                    SessionId,
                    24,
                    CreateMarkerDataPayload(2, 2_000, 1)),
                context));
        var marker = Assert.Single(markerFrame.Records);
        Assert.Equal((byte)1, marker.MarkerType);
        Assert.Equal((ulong)2_000, marker.MonotonicDeltaUs);
    }

    [Fact]
    public void ParseFrame_DataBeforeSessionHeader_Fails()
    {
        var frameBytes = LiveV3ProtocolReader.CreateFrame(
            LiveV3FrameType.TravelData,
            SessionId,
            30,
            CreateTravelDataPayload(0, 0, (100, 200)));

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(frameBytes, new LiveV3SessionDecodeContext()));
    }

    [Fact]
    public void ParseFrame_MarkerDataWithMultipleSamples_Fails()
    {
        var context = CreateReadyContext(MarkerStream());
        var frameBytes = LiveV3ProtocolReader.CreateFrame(
            LiveV3FrameType.MarkerData,
            SessionId,
            31,
            CreateMarkerDataPayload(0, 0, 1, 1));

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(frameBytes, context));
    }

    [Fact]
    public void ParseFrame_TemperatureDataWithoutAcceptedDescriptor_Fails()
    {
        var context = CreateReadyContext(TravelStream());
        var frameBytes = LiveV3ProtocolReader.CreateFrame(
            LiveV3FrameType.TemperatureData,
            SessionId,
            32,
            ReadOnlySpan<byte>.Empty);

        Assert.Throws<FormatException>(() => LiveV3ProtocolReader.ParseFrame(frameBytes, context));
    }

    [Fact]
    public void ParseFrame_StatusAndSessionResult_ParseCounters()
    {
        var context = CreateReadyContext(TravelStream(), GpsStream());

        var statusFrame = Assert.IsType<LiveStatusFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.Status, SessionId, 40, CreateStatusPayload()),
                context));
        var status = Assert.Single(statusFrame.Streams);
        Assert.Equal(LiveStreamMask.Travel, status.Stream);
        Assert.Equal((byte)1, status.ProducerState);
        Assert.Equal((ushort)6, status.SinkBacklogBatches);
        Assert.Equal((ulong)2, status.ProducerMissedCount);
        Assert.Equal((ulong)4, status.SinkMissedCount);

        var resultFrame = Assert.IsType<LiveSessionResultFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.SessionResult, SessionId, 41, CreateSessionResultPayload()),
                context));
        Assert.Equal(SessionId, resultFrame.Payload.SessionId);
        Assert.Equal((byte)1, resultFrame.Payload.FinalStatus.SessionResultReason);
        Assert.Equal((ulong)20_000, resultFrame.Payload.FinalStatus.StoppedMonotonicDeltaUs);
        Assert.Equal(2, resultFrame.Payload.FinalStatus.Streams.Length);
        Assert.Equal(SstV5ProtocolConstants.StreamTravel, resultFrame.Payload.FinalStatus.Streams[0].StreamKind);
        Assert.Equal((ushort)7, resultFrame.Payload.FinalStatus.Streams[0].SinkBacklogBatches);
        Assert.Equal(SstV5ProtocolConstants.StreamGps, resultFrame.Payload.FinalStatus.Streams[1].StreamKind);
    }

    [Fact]
    public void ParseFrame_DeviceStateResponse_ParsesStreamAndSourceState()
    {
        var payload = CreateDeviceStatePayload();

        var frame = Assert.IsType<LiveV3DeviceStateFrame>(
            LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.DeviceStateResp, 0, 42, payload),
                new LiveV3SessionDecodeContext()));

        var stream = Assert.Single(frame.Payload.Streams);
        Assert.Equal(SstV5ProtocolConstants.StreamTravel, stream.StreamKind);
        Assert.Equal((uint)200_000, stream.EffectiveDefaultRateMhz);
        Assert.Equal((uint)50, stream.DefaultBatchDurationMs);
        var source = Assert.Single(frame.Payload.Sources);
        Assert.Equal((byte)3, source.CalibrationStatus);
        Assert.True(source.Available);
        Assert.Equal(LiveSensorInstanceMask.ForkTravel, source.Source);
    }

    [Fact]
    public void ParseFrame_DeviceStateFrames_RequireSessionIdZero()
    {
        Assert.Throws<FormatException>(
            () => LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.DeviceStateReq, SessionId, 42, ReadOnlySpan<byte>.Empty),
                new LiveV3SessionDecodeContext()));

        Assert.Throws<FormatException>(
            () => LiveV3ProtocolReader.ParseFrame(
                LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.DeviceStateResp, SessionId, 43, CreateDeviceStatePayload()),
                new LiveV3SessionDecodeContext()));
    }

    [Fact]
    public void ParseFrame_TxSequenceDiscontinuity_IsTolerated()
    {
        var context = new LiveV3SessionDecodeContext();

        var first = LiveV3ProtocolReader.ParseFrame(
            LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.Pong, 0, 1, ReadOnlySpan<byte>.Empty),
            context);
        var second = LiveV3ProtocolReader.ParseFrame(
            LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.Pong, 0, 3, ReadOnlySpan<byte>.Empty),
            context);

        Assert.Equal((uint)1, first.Sequence);
        Assert.Equal((uint)3, second.Sequence);
    }
}

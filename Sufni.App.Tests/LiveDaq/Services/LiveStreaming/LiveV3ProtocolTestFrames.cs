using System.Buffers.Binary;
using System.Text;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

internal static class LiveV3ProtocolTestFrames
{
    public const byte DefaultSessionId = 42;

    private const long SessionStartUtcMs = 1_700_000_000_123;
    private const ulong SessionStartMonotonicUs = 123_456_789;

    public static byte[] Handshake() =>
    [
        (byte)'L', (byte)'I', (byte)'V', (byte)'3', 0, 0, 0,
    ];

    public static byte[] ServerHello(
        byte protocolMinor = 0,
        ulong uniqueBoardId = 0x0123456789abcdef) =>
        WriteBytes(writer =>
        {
            writer.Write(Encoding.ASCII.GetBytes("LIV3"));
            writer.Write(LiveV3ProtocolConstants.ProtocolMajor);
            writer.Write(protocolMinor);
            writer.Write((ushort)0);
            writer.Write((uint)LiveV3ProtocolConstants.MaxPayloadLength);
            writer.Write(uniqueBoardId);
            writer.Write((byte)1);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
        });

    public static byte[] CapabilitiesRequest(uint sequence = 0) =>
        Frame(LiveV3FrameType.CapabilitiesReq, sessionId: 0, sequence, []);

    public static byte[] DeviceStateRequest(uint sequence = 1) =>
        Frame(LiveV3FrameType.DeviceStateReq, sessionId: 0, sequence, []);

    public static byte[] StopRequest(byte sessionId = 1, uint sequence = 4) =>
        Frame(LiveV3FrameType.StopReq, sessionId, sequence, []);

    public static byte[] Ping(byte sessionId = 1, uint sequence = 3, uint nonce = 0xa1b2c3d4) =>
        Frame(LiveV3FrameType.Ping, sessionId, sequence, UInt32Payload(nonce));

    public static byte[] Pong(
        byte sessionId = DefaultSessionId,
        uint sequence = 13,
        uint nonce = 0xa1b2c3d4) =>
        Frame(LiveV3FrameType.Pong, sessionId, sequence, UInt32Payload(nonce));

    public static byte[] CapabilitiesResponse() =>
        Frame(
            LiveV3FrameType.CapabilitiesResp,
            sessionId: 0,
            sequence: 0,
            WriteBytes(writer =>
            {
                writer.Write((byte)2);
                writer.Write((byte)6);
                writer.Write((ushort)0);
                writer.Write((uint)(LiveStreamMask.Travel |
                                    LiveStreamMask.Imu |
                                    LiveStreamMask.Temperature |
                                    LiveStreamMask.Gps |
                                    LiveStreamMask.Battery |
                                    LiveStreamMask.Marker));
                writer.Write((uint)(LiveSensorInstanceMask.Travel |
                                    LiveSensorInstanceMask.FrameImu |
                                    LiveSensorInstanceMask.ForkImu |
                                    LiveSensorInstanceMask.Gps |
                                    LiveSensorInstanceMask.Battery));
                writer.Write((uint)LiveV3ProtocolConstants.MaxPayloadLength);

                WriteCapability(writer, TravelStream(), minRateMhz: 1_000, maxRateMhz: 1_000_000, maxBatchDurationMs: 10_000);
                WriteCapability(writer, ImuStream(), minRateMhz: 1_000, maxRateMhz: 1_000_000, maxBatchDurationMs: 10_000);
                WriteCapability(writer, TemperatureStream(), minRateMhz: 30, maxRateMhz: 30, maxBatchDurationMs: 0);
                WriteCapability(writer, GpsStream(), minRateMhz: 100, maxRateMhz: 10_000, maxBatchDurationMs: 0);
                WriteCapability(writer, BatteryStream(), minRateMhz: 30, maxRateMhz: 30, maxBatchDurationMs: 0);
                WriteCapability(writer, MarkerStream(), minRateMhz: 0, maxRateMhz: 0, maxBatchDurationMs: 0);
            }));

    public static byte[] DeviceStateResponse() =>
        Frame(
            LiveV3FrameType.DeviceStateResp,
            sessionId: 0,
            sequence: 1,
            WriteBytes(writer =>
            {
                writer.Write((byte)6);
                writer.Write((byte)6);
                writer.Write((ushort)0);
                WriteStreamState(writer, SstV5ProtocolConstants.StreamTravel, 200_000, 50);
                WriteStreamState(writer, SstV5ProtocolConstants.StreamImu, 200_000, 50);
                WriteStreamState(writer, SstV5ProtocolConstants.StreamTemperature, 30, 0);
                WriteStreamState(writer, SstV5ProtocolConstants.StreamGps, 5_000, 0);
                WriteStreamState(writer, SstV5ProtocolConstants.StreamBattery, 30, 0);
                WriteStreamState(writer, SstV5ProtocolConstants.StreamMarker, 0, 0);
                WriteSourceState(writer, calibrationStatus: 2, available: true, LiveSensorInstanceMask.ForkTravel);
                WriteSourceState(writer, calibrationStatus: 2, available: true, LiveSensorInstanceMask.ShockTravel);
                WriteSourceState(writer, calibrationStatus: 2, available: true, LiveSensorInstanceMask.FrameImu);
                WriteSourceState(writer, calibrationStatus: 2, available: false, LiveSensorInstanceMask.ForkImu);
                WriteSourceState(writer, calibrationStatus: 0, available: true, LiveSensorInstanceMask.Gps);
                WriteSourceState(writer, calibrationStatus: 0, available: true, LiveSensorInstanceMask.Battery);
            }));

    public static byte[] StartRequestAllStreams() =>
        Frame(
            LiveV3FrameType.StartReq,
            sessionId: 0,
            sequence: 2,
            WriteBytes(writer =>
            {
                writer.Write((byte)6);
                writer.Write((byte)0);
                writer.Write((ushort)(LiveV3ProtocolConstants.StartFlagPriority |
                                      LiveV3ProtocolConstants.StartFlagNoGpsHeaderWait));
                WriteStartRequest(writer, SstV5ProtocolConstants.StreamTravel, 3, LiveSensorInstanceMask.Travel, 0, 200_000, 50);
                WriteStartRequest(writer, SstV5ProtocolConstants.StreamImu, 3, LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu, 0, 200_000, 50);
                WriteStartRequest(writer, SstV5ProtocolConstants.StreamTemperature, 1, LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu, 0, 30, 0);
                WriteStartRequest(writer, SstV5ProtocolConstants.StreamGps, 1, LiveSensorInstanceMask.Gps, SstV5ProtocolConstants.ExtensionGpsDiagPublicV1, 5_000, 0);
                WriteStartRequest(writer, SstV5ProtocolConstants.StreamBattery, 0, LiveSensorInstanceMask.Battery, 0, 0, 0);
                WriteStartRequest(writer, SstV5ProtocolConstants.StreamMarker, 0, LiveSensorInstanceMask.None, 0, 0, 0);
            }));

    public static byte[] StartRequestTemperatureOnly() =>
        Frame(
            LiveV3FrameType.StartReq,
            sessionId: 0,
            sequence: 0,
            WriteBytes(writer =>
            {
                writer.Write((byte)1);
                writer.Write((byte)0);
                writer.Write((ushort)0);
                WriteStartRequest(
                    writer,
                    SstV5ProtocolConstants.StreamTemperature,
                    LiveV3ProtocolConstants.StreamRequestFlagRateOverride,
                    LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
                    0,
                    30,
                    0);
            }));

    public static byte[] StartResultPending(byte sessionId = DefaultSessionId, uint sequence = 2) =>
        Frame(
            LiveV3FrameType.StartResult,
            sessionId,
            sequence,
            [0, 0, 0, 0]);

    public static byte[] StartResultDenied() =>
        Frame(
            LiveV3FrameType.StartResult,
            sessionId: 0,
            sequence: 0,
            WriteBytes(writer =>
            {
                writer.Write((byte)1);
                writer.Write((byte)0);
                writer.Write((ushort)2);
                WriteReason(writer, targetKind: 1, streamKind: 0, reason: 7, (uint)(LiveStreamMask.Travel | LiveStreamMask.Imu));
                WriteReason(writer, targetKind: 2, SstV5ProtocolConstants.StreamImu, reason: 3, (uint)LiveSensorInstanceMask.RearImu);
            }));

    public static byte[] StartResultPriorityConflict() =>
        Frame(
            LiveV3FrameType.StartResult,
            sessionId: 0,
            sequence: 3,
            WriteBytes(writer =>
            {
                writer.Write((byte)1);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                WriteReason(writer, targetKind: 1, streamKind: 0, reason: 5, (uint)LiveStreamMask.Travel);
            }));

    public static byte[] SessionHeaderTravel(byte sessionId = DefaultSessionId, uint sequence = 7) =>
        SessionHeader(sessionId, sequence, [TravelStream()], []);

    public static byte[] SessionHeaderTemperature() =>
        SessionHeader(DefaultSessionId, sequence: 14, [TemperatureStream()], []);

    public static byte[] SessionHeaderTravelImuGps() =>
        SessionHeader(DefaultSessionId, sequence: 6, [TravelStream(), ImuStream(), GpsStream()], []);

    public static byte[] SessionHeaderAllStreams() =>
        SessionHeader(
            DefaultSessionId,
            sequence: 3,
            [TravelStream(), ImuStream(), TemperatureStream(), GpsStream(), BatteryStream(), MarkerStream()],
            [new OmissionSpec(3, SstV5ProtocolConstants.StreamGps, 3, SstV5ProtocolConstants.ExtensionGpsDiagPublicV1)]);

    public static byte[] SessionHeaderPartialTravel() =>
        SessionHeader(
            DefaultSessionId,
            sequence: 5,
            [TravelStream()],
            [new OmissionSpec(1, SstV5ProtocolConstants.StreamImu, 3, 0)]);

    public static byte[] TravelData() =>
        DataFrame(
            LiveV3FrameType.TravelData,
            sequence: 4,
            firstIndex: 1001,
            firstMonotonicDeltaUs: 1000,
            sampleCount: 1,
            validityMask: LiveSensorInstanceMask.Travel,
            writer =>
            {
                writer.Write((ushort)1000);
                writer.Write((ushort)1111);
            });

    public static byte[] ImuData() =>
        DataFrame(
            LiveV3FrameType.ImuData,
            sequence: 5,
            firstIndex: 1002,
            firstMonotonicDeltaUs: 2000,
            sampleCount: 1,
            validityMask: LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
            writer =>
            {
                WriteImuRecord(writer, 100, -200, 300, -400, 500, -600);
                WriteImuRecord(writer, 700, -800, 900, -1000, 1100, -1200);
            });

    public static byte[] TemperatureData() =>
        DataFrame(
            LiveV3FrameType.TemperatureData,
            sequence: 6,
            firstIndex: 1003,
            firstMonotonicDeltaUs: 3000,
            sampleCount: 1,
            validityMask: LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
            writer =>
            {
                writer.Write((short)340);
                writer.Write((short)-256);
            });

    public static byte[] GpsData() =>
        DataFrame(
            LiveV3FrameType.GpsData,
            sequence: 7,
            firstIndex: 1004,
            firstMonotonicDeltaUs: 4000,
            sampleCount: 1,
            validityMask: LiveSensorInstanceMask.Gps,
            writer =>
            {
                writer.Write((uint)20260710);
                writer.Write((uint)45_296_789);
                writer.Write(426_975_123);
                writer.Write(233_216_789);
                writer.Write(721_345);
                writer.Write(12_345);
                writer.Write(18_765_432);
                writer.Write((byte)3);
                writer.Write((byte)14);
            });

    public static byte[] BatteryData() =>
        DataFrame(
            LiveV3FrameType.BatteryData,
            sequence: 8,
            firstIndex: 1005,
            firstMonotonicDeltaUs: 5000,
            sampleCount: 1,
            validityMask: LiveSensorInstanceMask.Battery,
            writer =>
            {
                writer.Write((ushort)3987);
                writer.Write((ushort)1);
            });

    public static byte[] MarkerData() =>
        DataFrame(
            LiveV3FrameType.MarkerData,
            sequence: 9,
            firstIndex: 1006,
            firstMonotonicDeltaUs: 6000,
            sampleCount: 1,
            validityMask: LiveSensorInstanceMask.None,
            writer => writer.Write(SstV5ProtocolConstants.MarkerManualUserMark));

    public static byte[] StatusTravelGap() =>
        Frame(
            LiveV3FrameType.Status,
            DefaultSessionId,
            sequence: 10,
            WriteBytes(writer =>
            {
                writer.Write((byte)1);
                writer.Write((byte)0);
                writer.Write((ushort)0);
                WriteStatusRecord(writer, producerState: 1, producerFailureReason: 0, sinkBacklogBatches: 3, 0, 0, 2, 10_000);
            }));

    public static byte[] StatusAllStreams() =>
        Frame(
            LiveV3FrameType.Status,
            DefaultSessionId,
            sequence: 10,
            WriteBytes(writer =>
            {
                writer.Write((byte)6);
                writer.Write((byte)0);
                writer.Write((ushort)0);
                for (var index = 0; index < 6; index++)
                {
                    WriteStatusRecord(
                        writer,
                        producerState: index == 5 ? (byte)0 : (byte)1,
                        producerFailureReason: 0,
                        sinkBacklogBatches: (ushort)(index + 1),
                        producerMissedCount: (ulong)(11 + index),
                        producerMissingTimeUs: (ulong)(21 + index),
                        sinkMissedCount: (ulong)(31 + index),
                        sinkMissingTimeUs: (ulong)(41 + index));
                }
            }));

    public static byte[] SessionResultTravel(byte sessionId = DefaultSessionId, uint sequence = 11) =>
        SessionResult(
            sessionId,
            sequence,
            reason: 1,
            stoppedMonotonicDeltaUs: 50_000,
            [new StatusSpec(1, 0, 3, 0, 0, 2, 10_000)]);

    public static byte[] SessionResultTemperature() =>
        SessionResult(
            DefaultSessionId,
            sequence: 15,
            reason: 1,
            stoppedMonotonicDeltaUs: 50_000,
            [new StatusSpec(1, 0, 0, 0, 0, 0, 0)]);

    public static byte[] SessionResultAllStreams() =>
        SessionResult(
            DefaultSessionId,
            sequence: 12,
            reason: 2,
            stoppedMonotonicDeltaUs: 9_876_543,
            Enumerable.Range(0, 6)
                .Select(index => new StatusSpec(
                    index == 5 ? (byte)0 : (byte)1,
                    0,
                    (ushort)(index + 1),
                    (ulong)(11 + index),
                    (ulong)(21 + index),
                    (ulong)(31 + index),
                    (ulong)(41 + index)))
                .ToArray());

    public static byte[] SessionResultPreheaderFailure() =>
        SessionResult(
            sessionId: 43,
            sequence: 0,
            reason: 5,
            stoppedMonotonicDeltaUs: 25_000,
            []);

    public static byte[] StopResult(byte sessionId = DefaultSessionId, uint sequence = 11) =>
        Frame(LiveV3FrameType.StopResult, sessionId, sequence, [0, 0, 0, 0]);

    public static byte[] Error() =>
        Frame(
            LiveV3FrameType.Error,
            sessionId: 0,
            sequence: 14,
            WriteBytes(writer =>
            {
                writer.Write((byte)2);
                writer.Write((byte)99);
                writer.Write((ushort)0);
                writer.Write(0xdecafbadu);
            }));

    public static byte[] Frame(
        LiveV3FrameType frameType,
        byte sessionId,
        uint sequence,
        ReadOnlySpan<byte> payload)
    {
        var frame = new byte[LiveV3ProtocolConstants.FrameHeaderSize + payload.Length];
        frame[0] = sessionId;
        frame[1] = (byte)frameType;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8, 4), sequence);
        payload.CopyTo(frame.AsSpan(LiveV3ProtocolConstants.FrameHeaderSize));
        return frame;
    }

    private static byte[] SessionHeader(
        byte sessionId,
        uint sequence,
        TestStreamSpec[] streams,
        OmissionSpec[] omissions) =>
        Frame(
            LiveV3FrameType.SessionHeader,
            sessionId,
            sequence,
            WriteBytes(writer =>
            {
                var orderedStreams = streams.OrderBy(stream => stream.Kind).ToArray();
                writer.Write((byte)2);
                writer.Write((byte)orderedStreams.Length);
                writer.Write((byte)orderedStreams.Sum(stream => stream.Sources.Length));
                writer.Write((byte)omissions.Length);
                writer.Write(orderedStreams.Aggregate(0u, (mask, stream) =>
                    mask | SstV5ProtocolConstants.StreamMaskForKind(stream.Kind)));
                writer.Write(SessionStartUtcMs);
                writer.Write(SessionStartMonotonicUs);
                foreach (var stream in orderedStreams)
                {
                    WriteStreamDescriptor(writer, stream);
                }
                foreach (var stream in orderedStreams)
                {
                    foreach (var source in stream.Sources.OrderBy(source => source.SourceMask))
                    {
                        WriteSourceDescriptor(writer, source);
                    }
                }
                foreach (var omission in omissions)
                {
                    writer.Write(omission.TargetKind);
                    writer.Write(omission.StreamKind);
                    writer.Write(omission.Reason);
                    writer.Write((byte)0);
                    writer.Write(omission.TargetMask);
                }
            }));

    private static byte[] SessionResult(
        byte sessionId,
        uint sequence,
        byte reason,
        ulong stoppedMonotonicDeltaUs,
        StatusSpec[] statuses) =>
        Frame(
            LiveV3FrameType.SessionResult,
            sessionId,
            sequence,
            WriteBytes(writer =>
            {
                writer.Write(reason);
                writer.Write((byte)statuses.Length);
                writer.Write((ushort)0);
                writer.Write(stoppedMonotonicDeltaUs);
                foreach (var status in statuses)
                {
                    WriteStatusRecord(
                        writer,
                        status.ProducerState,
                        status.ProducerFailureReason,
                        status.SinkBacklogBatches,
                        status.ProducerMissedCount,
                        status.ProducerMissingTimeUs,
                        status.SinkMissedCount,
                        status.SinkMissingTimeUs);
                }
            }));

    private static byte[] DataFrame(
        LiveV3FrameType frameType,
        uint sequence,
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        uint sampleCount,
        LiveSensorInstanceMask validityMask,
        Action<BinaryWriter> writeRecords) =>
        Frame(
            frameType,
            DefaultSessionId,
            sequence,
            WriteBytes(writer =>
            {
                writer.Write(firstIndex);
                writer.Write(firstMonotonicDeltaUs);
                writer.Write(sampleCount);
                writer.Write((uint)validityMask);
                writer.Write((ushort)0);
                writeRecords(writer);
            }));

    private static TestStreamSpec TravelStream() => new(
        SstV5ProtocolConstants.StreamTravel,
        SstV5ProtocolConstants.TimingFixedRate,
        LiveSensorInstanceMask.Travel,
        0,
        200_000,
        50,
        4,
        [
            TestSourceSpec.Travel(LiveSensorInstanceMask.ForkTravel, 0),
            TestSourceSpec.Travel(LiveSensorInstanceMask.ShockTravel, 2),
        ]);

    private static TestStreamSpec ImuStream() => new(
        SstV5ProtocolConstants.StreamImu,
        SstV5ProtocolConstants.TimingFixedRate,
        LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
        0,
        200_000,
        50,
        24,
        [
            TestSourceSpec.Imu(LiveSensorInstanceMask.FrameImu, 0),
            TestSourceSpec.Imu(LiveSensorInstanceMask.ForkImu, 12),
        ]);

    private static TestStreamSpec TemperatureStream() => new(
        SstV5ProtocolConstants.StreamTemperature,
        SstV5ProtocolConstants.TimingMonotonicEventStatus,
        LiveSensorInstanceMask.FrameImu | LiveSensorInstanceMask.ForkImu,
        0,
        30,
        0,
        4,
        [
            TestSourceSpec.Temperature(LiveSensorInstanceMask.FrameImu, 0, 340, 36.53f),
            TestSourceSpec.Temperature(LiveSensorInstanceMask.ForkImu, 2, 256, 25),
        ]);

    private static TestStreamSpec GpsStream() => new(
        SstV5ProtocolConstants.StreamGps,
        SstV5ProtocolConstants.TimingGpsReceiverTimed,
        LiveSensorInstanceMask.Gps,
        0,
        5_000,
        0,
        SstV5ProtocolConstants.M8NRecordSize,
        [],
        GpsDriverId: SstV5ProtocolConstants.GpsDriverM8N,
        GpsPayloadEncodingId: SstV5ProtocolConstants.EncodingGpsNavFixV1,
        GpsPayloadRecordBytes: SstV5ProtocolConstants.M8NRecordSize);

    private static TestStreamSpec BatteryStream() => new(
        SstV5ProtocolConstants.StreamBattery,
        SstV5ProtocolConstants.TimingMonotonicEventStatus,
        LiveSensorInstanceMask.Battery,
        0,
        30,
        0,
        4,
        []);

    private static TestStreamSpec MarkerStream() => new(
        SstV5ProtocolConstants.StreamMarker,
        SstV5ProtocolConstants.TimingMonotonicEventStatus,
        LiveSensorInstanceMask.None,
        0,
        0,
        0,
        1,
        []);

    private static void WriteCapability(
        BinaryWriter writer,
        TestStreamSpec stream,
        uint minRateMhz,
        uint maxRateMhz,
        uint maxBatchDurationMs)
    {
        writer.Write(stream.Kind);
        writer.Write(stream.TimingModelId);
        writer.Write((ushort)0);
        writer.Write((uint)stream.AcceptedSensorMask);
        writer.Write(stream.Kind == SstV5ProtocolConstants.StreamGps
            ? SstV5ProtocolConstants.ExtensionGpsDiagPublicV1
            : stream.AcceptedExtensionMask);
        writer.Write(minRateMhz);
        writer.Write(maxRateMhz);
        writer.Write(maxBatchDurationMs);
    }

    private static void WriteStreamState(BinaryWriter writer, byte streamKind, uint rateMhz, uint batchDurationMs)
    {
        writer.Write(streamKind);
        writer.Write(new byte[3]);
        writer.Write(rateMhz);
        writer.Write(batchDurationMs);
    }

    private static void WriteSourceState(
        BinaryWriter writer,
        byte calibrationStatus,
        bool available,
        LiveSensorInstanceMask source)
    {
        writer.Write(calibrationStatus);
        writer.Write(available ? (byte)1 : (byte)0);
        writer.Write((ushort)0);
        writer.Write((uint)source);
    }

    private static void WriteStartRequest(
        BinaryWriter writer,
        byte streamKind,
        ushort flags,
        LiveSensorInstanceMask sourceMask,
        uint extensionMask,
        uint rateMhz,
        uint batchDurationMs)
    {
        writer.Write(streamKind);
        writer.Write((byte)0);
        writer.Write(flags);
        writer.Write((uint)sourceMask);
        writer.Write(extensionMask);
        writer.Write(rateMhz);
        writer.Write(batchDurationMs);
    }

    private static void WriteReason(
        BinaryWriter writer,
        byte targetKind,
        byte streamKind,
        byte reason,
        uint targetMask)
    {
        writer.Write(targetKind);
        writer.Write(streamKind);
        writer.Write(reason);
        writer.Write((byte)0);
        writer.Write(targetMask);
    }

    private static void WriteStreamDescriptor(BinaryWriter writer, TestStreamSpec stream)
    {
        writer.Write(stream.Kind);
        writer.Write(stream.TimingModelId);
        writer.Write((byte)stream.Sources.Length);
        writer.Write((byte)0);
        writer.Write((uint)stream.AcceptedSensorMask);
        writer.Write(stream.AcceptedExtensionMask);
        writer.Write(stream.AcceptedRateMhz);
        writer.Write(stream.AcceptedBatchDurationMs);
        writer.Write(stream.CompactPayloadRecordBytes);
        writer.Write((ushort)0);
        if (stream.Kind == SstV5ProtocolConstants.StreamGps)
        {
            writer.Write(stream.GpsDriverId);
            writer.Write(stream.GpsPayloadEncodingId);
            writer.Write(stream.GpsPayloadRecordBytes);
            writer.Write(stream.GpsExtensionRecordBytes);
            writer.Write((ushort)0);
        }
    }

    private static void WriteSourceDescriptor(BinaryWriter writer, TestSourceSpec source)
    {
        writer.Write(source.StreamKind);
        writer.Write((byte)1);
        writer.Write(source.PayloadEncodingId);
        writer.Write(source.PayloadValueCount);
        writer.Write((uint)source.SourceMask);
        writer.Write(source.PayloadByteOffset);
        writer.Write(source.PayloadRecordBytes);
        writer.Write((ushort)16);
        writer.Write((ushort)0);
        switch (source.StreamKind)
        {
            case SstV5ProtocolConstants.StreamTravel:
                writer.Write((ushort)0);
                writer.Write((ushort)4095);
                writer.Write((ushort)4096);
                writer.Write((byte)1);
                writer.Write((byte)0);
                break;
            case SstV5ProtocolConstants.StreamImu:
                writer.Write(4096f);
                writer.Write(32.8f);
                writer.Write((byte)1);
                writer.Write(new byte[3]);
                break;
            case SstV5ProtocolConstants.StreamTemperature:
                writer.Write(source.TemperatureLsbPerCelsius);
                writer.Write(source.TemperatureCelsiusAtRawZero);
                break;
        }
    }

    private static void WriteStatusRecord(
        BinaryWriter writer,
        byte producerState,
        byte producerFailureReason,
        ushort sinkBacklogBatches,
        ulong producerMissedCount,
        ulong producerMissingTimeUs,
        ulong sinkMissedCount,
        ulong sinkMissingTimeUs)
    {
        writer.Write(producerState);
        writer.Write(producerFailureReason);
        writer.Write(sinkBacklogBatches);
        writer.Write(producerMissedCount);
        writer.Write(producerMissingTimeUs);
        writer.Write(sinkMissedCount);
        writer.Write(sinkMissingTimeUs);
    }

    private static void WriteImuRecord(BinaryWriter writer, params short[] values)
    {
        foreach (var value in values)
        {
            writer.Write(value);
        }
    }

    private static byte[] UInt32Payload(uint value) =>
        WriteBytes(writer => writer.Write(value));

    private static byte[] WriteBytes(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        write(writer);
        return stream.ToArray();
    }

    private readonly record struct TestStreamSpec(
        byte Kind,
        byte TimingModelId,
        LiveSensorInstanceMask AcceptedSensorMask,
        uint AcceptedExtensionMask,
        uint AcceptedRateMhz,
        uint AcceptedBatchDurationMs,
        ushort CompactPayloadRecordBytes,
        TestSourceSpec[] Sources,
        byte GpsDriverId = 0,
        byte GpsPayloadEncodingId = 0,
        ushort GpsPayloadRecordBytes = 0,
        ushort GpsExtensionRecordBytes = 0);

    private readonly record struct TestSourceSpec(
        byte StreamKind,
        byte PayloadEncodingId,
        byte PayloadValueCount,
        LiveSensorInstanceMask SourceMask,
        ushort PayloadByteOffset,
        ushort PayloadRecordBytes,
        float TemperatureLsbPerCelsius = 0,
        float TemperatureCelsiusAtRawZero = 0)
    {
        public static TestSourceSpec Travel(LiveSensorInstanceMask source, ushort offset) =>
            new(SstV5ProtocolConstants.StreamTravel, SstV5ProtocolConstants.EncodingTravelAdjustedU16, 1, source, offset, 2);

        public static TestSourceSpec Imu(LiveSensorInstanceMask source, ushort offset) =>
            new(SstV5ProtocolConstants.StreamImu, SstV5ProtocolConstants.EncodingImuI16X6CalCounts, 6, source, offset, 12);

        public static TestSourceSpec Temperature(
            LiveSensorInstanceMask source,
            ushort offset,
            float lsbPerCelsius,
            float celsiusAtRawZero) =>
            new(
                SstV5ProtocolConstants.StreamTemperature,
                SstV5ProtocolConstants.EncodingTemperatureRawI16,
                1,
                source,
                offset,
                2,
                lsbPerCelsius,
                celsiusAtRawZero);
    }

    private readonly record struct OmissionSpec(byte TargetKind, byte StreamKind, byte Reason, uint TargetMask);

    private readonly record struct StatusSpec(
        byte ProducerState,
        byte ProducerFailureReason,
        ushort SinkBacklogBatches,
        ulong ProducerMissedCount,
        ulong ProducerMissingTimeUs,
        ulong SinkMissedCount,
        ulong SinkMissingTimeUs);
}

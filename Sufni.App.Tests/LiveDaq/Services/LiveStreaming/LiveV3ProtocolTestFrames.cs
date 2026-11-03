using System.Buffers.Binary;
using System.Text;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

// Frozen firmware v3 field values; these builders must not call production encoders or mask helpers.
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
            writer.Write(((byte)3));
            writer.Write(protocolMinor);
            writer.Write((ushort)0);
            writer.Write((uint)((int)4096));
            writer.Write(uniqueBoardId);
            writer.Write((byte)1);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
        });

    public static byte[] CapabilitiesRequest(uint sequence = 0) =>
        Frame(((LiveV3FrameType)1), sessionId: 0, sequence, []);

    public static byte[] StopRequest(byte sessionId = 1, uint sequence = 4) =>
        Frame(((LiveV3FrameType)5), sessionId, sequence, []);

    public static byte[] Pong(
        byte sessionId = DefaultSessionId,
        uint sequence = 13,
        uint nonce = 0xa1b2c3d4) =>
        Frame(((LiveV3FrameType)17), sessionId, sequence, UInt32Payload(nonce));

    public static byte[] CapabilitiesResponse() =>
        Frame(
            ((LiveV3FrameType)2),
            sessionId: 0,
            sequence: 0,
            WriteBytes(writer =>
            {
                writer.Write((byte)2);
                writer.Write((byte)6);
                writer.Write((ushort)0);
                writer.Write((uint)(((LiveStreamMask)2) |
                                    ((LiveStreamMask)4) |
                                    ((LiveStreamMask)8) |
                                    ((LiveStreamMask)16) |
                                    ((LiveStreamMask)32) |
                                    ((LiveStreamMask)64)));
                writer.Write((uint)(((LiveSensorInstanceMask)3) |
                                    ((LiveSensorInstanceMask)4) |
                                    ((LiveSensorInstanceMask)8) |
                                    ((LiveSensorInstanceMask)32) |
                                    ((LiveSensorInstanceMask)64)));
                writer.Write((uint)((int)4096));

                WriteCapability(writer, TravelStream(), minRateMhz: 1_000, maxRateMhz: 1_000_000, maxBatchDurationMs: 10_000);
                WriteCapability(writer, ImuStream(), minRateMhz: 1_000, maxRateMhz: 1_000_000, maxBatchDurationMs: 10_000);
                WriteCapability(writer, TemperatureStream(), minRateMhz: 30, maxRateMhz: 30, maxBatchDurationMs: 0);
                WriteCapability(writer, GpsStream(), minRateMhz: 100, maxRateMhz: 10_000, maxBatchDurationMs: 0);
                WriteCapability(writer, BatteryStream(), minRateMhz: 30, maxRateMhz: 30, maxBatchDurationMs: 0);
                WriteCapability(writer, MarkerStream(), minRateMhz: 0, maxRateMhz: 0, maxBatchDurationMs: 0);
            }));

    public static byte[] StartRequestAllStreams() =>
        Frame(
            ((LiveV3FrameType)3),
            sessionId: 0,
            sequence: 2,
            WriteBytes(writer =>
            {
                writer.Write((byte)6);
                writer.Write((byte)0);
                writer.Write((ushort)(((ushort)0x0001) |
                                      ((ushort)0x0002)));
                WriteStartRequest(writer, ((byte)1), 3, ((LiveSensorInstanceMask)3), 0, 200_000, 50);
                WriteStartRequest(writer, ((byte)2), 3, ((LiveSensorInstanceMask)4) | ((LiveSensorInstanceMask)8), 0, 200_000, 50);
                WriteStartRequest(writer, ((byte)3), 1, ((LiveSensorInstanceMask)4) | ((LiveSensorInstanceMask)8), 0, 30, 0);
                WriteStartRequest(writer, ((byte)4), 1, ((LiveSensorInstanceMask)32), ((uint)0x00000001), 5_000, 0);
                WriteStartRequest(writer, ((byte)5), 0, ((LiveSensorInstanceMask)64), 0, 0, 0);
                WriteStartRequest(writer, ((byte)6), 0, ((LiveSensorInstanceMask)0), 0, 0, 0);
            }));

    public static byte[] StartRequestTemperatureOnly() =>
        Frame(
            ((LiveV3FrameType)3),
            sessionId: 0,
            sequence: 0,
            WriteBytes(writer =>
            {
                writer.Write((byte)1);
                writer.Write((byte)0);
                writer.Write((ushort)0);
                WriteStartRequest(
                    writer,
                    ((byte)3),
                    ((ushort)0x0001),
                    ((LiveSensorInstanceMask)4) | ((LiveSensorInstanceMask)8),
                    0,
                    30,
                    0);
            }));

    public static byte[] StartResultPending(byte sessionId = DefaultSessionId, uint sequence = 2) =>
        Frame(
            ((LiveV3FrameType)4),
            sessionId,
            sequence,
            [0, 0, 0, 0]);

    public static byte[] StartResultDenied() =>
        Frame(
            ((LiveV3FrameType)4),
            sessionId: 0,
            sequence: 0,
            WriteBytes(writer =>
            {
                writer.Write((byte)1);
                writer.Write((byte)0);
                writer.Write((ushort)2);
                WriteReason(writer, targetKind: 1, streamKind: 0, reason: 7, (uint)(((LiveStreamMask)2) | ((LiveStreamMask)4)));
                WriteReason(writer, targetKind: 2, ((byte)2), reason: 3, (uint)((LiveSensorInstanceMask)16));
            }));

    public static byte[] StartResultPriorityConflict() =>
        Frame(
            ((LiveV3FrameType)4),
            sessionId: 0,
            sequence: 3,
            WriteBytes(writer =>
            {
                writer.Write((byte)1);
                writer.Write((byte)0);
                writer.Write((ushort)1);
                WriteReason(writer, targetKind: 1, streamKind: 0, reason: 5, (uint)((LiveStreamMask)2));
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
            [new OmissionSpec(3, ((byte)4), 3, ((uint)0x00000001))]);

    public static byte[] SessionHeaderPartialTravel() =>
        SessionHeader(
            DefaultSessionId,
            sequence: 5,
            [TravelStream()],
            [new OmissionSpec(1, ((byte)2), 3, 0)]);

    public static byte[] TravelData() =>
        DataFrame(
            ((LiveV3FrameType)9),
            sequence: 4,
            firstIndex: 1001,
            firstMonotonicDeltaUs: 1000,
            sampleCount: 1,
            validityMask: ((LiveSensorInstanceMask)3),
            writer =>
            {
                writer.Write((ushort)1000);
                writer.Write((ushort)1111);
            });

    public static byte[] ImuData() =>
        DataFrame(
            ((LiveV3FrameType)10),
            sequence: 5,
            firstIndex: 1002,
            firstMonotonicDeltaUs: 2000,
            sampleCount: 1,
            validityMask: ((LiveSensorInstanceMask)4) | ((LiveSensorInstanceMask)8),
            writer =>
            {
                WriteImuRecord(writer, 100, -200, 300, -400, 500, -600);
                WriteImuRecord(writer, 700, -800, 900, -1000, 1100, -1200);
            });

    public static byte[] TemperatureData() =>
        DataFrame(
            ((LiveV3FrameType)11),
            sequence: 6,
            firstIndex: 1003,
            firstMonotonicDeltaUs: 3000,
            sampleCount: 1,
            validityMask: ((LiveSensorInstanceMask)4) | ((LiveSensorInstanceMask)8),
            writer =>
            {
                writer.Write((short)340);
                writer.Write((short)-256);
            });

    public static byte[] GpsData() =>
        DataFrame(
            ((LiveV3FrameType)12),
            sequence: 7,
            firstIndex: 1004,
            firstMonotonicDeltaUs: 4000,
            sampleCount: 1,
            validityMask: ((LiveSensorInstanceMask)32),
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
            ((LiveV3FrameType)13),
            sequence: 8,
            firstIndex: 1005,
            firstMonotonicDeltaUs: 5000,
            sampleCount: 1,
            validityMask: ((LiveSensorInstanceMask)64),
            writer =>
            {
                writer.Write((ushort)3987);
                writer.Write((ushort)1);
            });

    public static byte[] MarkerData() =>
        DataFrame(
            ((LiveV3FrameType)14),
            sequence: 9,
            firstIndex: 1006,
            firstMonotonicDeltaUs: 6000,
            sampleCount: 1,
            validityMask: ((LiveSensorInstanceMask)0),
            writer => writer.Write(((byte)1)));

    public static byte[] StatusTravelGap() =>
        Frame(
            ((LiveV3FrameType)15),
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
            ((LiveV3FrameType)15),
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
        Frame(((LiveV3FrameType)6), sessionId, sequence, [0, 0, 0, 0]);

    public static byte[] Error() =>
        Frame(
            ((LiveV3FrameType)18),
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
        var frame = new byte[((int)12) + payload.Length];
        frame[0] = sessionId;
        frame[1] = (byte)frameType;
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4, 4), (uint)payload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(8, 4), sequence);
        payload.CopyTo(frame.AsSpan(((int)12)));
        return frame;
    }

    private static byte[] SessionHeader(
        byte sessionId,
        uint sequence,
        TestStreamSpec[] streams,
        OmissionSpec[] omissions) =>
        Frame(
            ((LiveV3FrameType)7),
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
                    mask | (stream.Kind switch { 1 => 2u, 2 => 4u, 3 => 8u, 4 => 16u, 5 => 32u, 6 => 64u, _ => throw new ArgumentOutOfRangeException() })));
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
            ((LiveV3FrameType)8),
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
        ((byte)1),
        ((byte)1),
        ((LiveSensorInstanceMask)3),
        0,
        200_000,
        50,
        4,
        [
            TestSourceSpec.Travel(((LiveSensorInstanceMask)1), 0),
            TestSourceSpec.Travel(((LiveSensorInstanceMask)2), 2),
        ]);

    private static TestStreamSpec ImuStream() => new(
        ((byte)2),
        ((byte)1),
        ((LiveSensorInstanceMask)4) | ((LiveSensorInstanceMask)8),
        0,
        200_000,
        50,
        24,
        [
            TestSourceSpec.Imu(((LiveSensorInstanceMask)4), 0),
            TestSourceSpec.Imu(((LiveSensorInstanceMask)8), 12),
        ]);

    private static TestStreamSpec TemperatureStream() => new(
        ((byte)3),
        ((byte)2),
        ((LiveSensorInstanceMask)4) | ((LiveSensorInstanceMask)8),
        0,
        30,
        0,
        4,
        [
            TestSourceSpec.Temperature(((LiveSensorInstanceMask)4), 0, 340, 36.53f),
            TestSourceSpec.Temperature(((LiveSensorInstanceMask)8), 2, 256, 25),
        ]);

    private static TestStreamSpec GpsStream() => new(
        ((byte)4),
        ((byte)3),
        ((LiveSensorInstanceMask)32),
        0,
        5_000,
        0,
        ((int)30),
        [],
        GpsDriverId: ((byte)2),
        GpsPayloadEncodingId: ((byte)4),
        GpsPayloadRecordBytes: ((int)30));

    private static TestStreamSpec BatteryStream() => new(
        ((byte)5),
        ((byte)2),
        ((LiveSensorInstanceMask)64),
        0,
        30,
        0,
        4,
        []);

    private static TestStreamSpec MarkerStream() => new(
        ((byte)6),
        ((byte)2),
        ((LiveSensorInstanceMask)0),
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
        writer.Write(stream.Kind == ((byte)4)
            ? ((uint)0x00000001)
            : stream.AcceptedExtensionMask);
        writer.Write(minRateMhz);
        writer.Write(maxRateMhz);
        writer.Write(maxBatchDurationMs);
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
        if (stream.Kind == ((byte)4))
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
            case ((byte)1):
                writer.Write((ushort)0);
                writer.Write((ushort)4095);
                writer.Write((ushort)4096);
                writer.Write((byte)1);
                writer.Write((byte)0);
                break;
            case ((byte)2):
                writer.Write(4096f);
                writer.Write(32.8f);
                writer.Write((byte)1);
                writer.Write(new byte[3]);
                break;
            case ((byte)3):
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
            new(((byte)1), ((byte)1), 1, source, offset, 2);

        public static TestSourceSpec Imu(LiveSensorInstanceMask source, ushort offset) =>
            new(((byte)2), ((byte)2), 6, source, offset, 12);

        public static TestSourceSpec Temperature(
            LiveSensorInstanceMask source,
            ushort offset,
            float lsbPerCelsius,
            float celsiusAtRawZero) =>
            new(
                ((byte)3),
                ((byte)3),
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

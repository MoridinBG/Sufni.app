using System.Buffers.Binary;
using System.Text;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

internal static class LiveV3ProtocolTestFrames
{
    public const byte SessionId = 7;
    public const long SessionStartUtcMs = 1_700_000_000_123;
    public const ulong SessionStartMonotonicUs = 10_000;

    public static LiveV3SessionDecodeContext CreateStartedContext()
    {
        var context = new LiveV3SessionDecodeContext();
        context.AcceptSession(SessionId);
        return context;
    }

    public static LiveV3SessionDecodeContext CreateReadyContext(params V3StreamSpec[] streams)
    {
        var context = CreateStartedContext();
        ParseSessionHeader(context, streams);
        return context;
    }

    public static LiveSessionHeaderFrame ParseSessionHeader(
        LiveV3SessionDecodeContext context,
        params V3StreamSpec[] streams)
    {
        var payload = CreateSessionHeaderPayload(
            LiveSensorInstanceMask.Travel | LiveSensorInstanceMask.Imu | LiveSensorInstanceMask.Gps,
            streams);
        return (LiveSessionHeaderFrame)LiveV3ProtocolReader.ParseFrame(
            LiveV3ProtocolReader.CreateFrame(LiveV3FrameType.SessionHeader, SessionId, 10, payload),
            context);
    }

    public static byte[] CreateRawHeader(LiveV3FrameType frameType, uint payloadLength, uint sequence) =>
        CreateRawHeader((byte)frameType, payloadLength, sequence);

    public static byte[] CreateRawHeader(byte rawFrameType, uint payloadLength, uint sequence)
    {
        var header = new byte[LiveV3ProtocolConstants.FrameHeaderSize];
        header[0] = rawFrameType;
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4, 4), payloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), sequence);
        return header;
    }

    public static byte[] CreateCapabilitiesPayload(
        LiveStreamMask supportedStreams,
        LiveSensorInstanceMask supportedSources,
        params (byte StreamKind, LiveSensorInstanceMask Sources, uint MinRateMhz, uint MaxRateMhz)[] streams)
    {
        var payload = new byte[LiveV3ProtocolConstants.CapabilitiesHeaderSize +
                               streams.Length * LiveV3ProtocolConstants.StreamCapabilityRecordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), LiveV3ProtocolConstants.MaxPayloadLength);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), (uint)supportedStreams);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(8, 4), (uint)supportedSources);
        payload[16] = (byte)streams.Length;

        var offset = LiveV3ProtocolConstants.CapabilitiesHeaderSize;
        foreach (var stream in streams)
        {
            payload[offset] = stream.StreamKind;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 4, 4), (uint)stream.Sources);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 12, 4), stream.MinRateMhz);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(offset + 16, 4), stream.MaxRateMhz);
            offset += LiveV3ProtocolConstants.StreamCapabilityRecordSize;
        }

        return payload;
    }

    public static byte[] CreateSessionHeaderPayload(
        LiveSensorInstanceMask requestedSensorMask,
        params V3StreamSpec[] streams)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((uint)LiveSessionFlags.None);
        writer.Write((uint)requestedSensorMask);
        writer.Write(SessionStartUtcMs);
        writer.Write(SessionStartMonotonicUs);
        writer.Write(CreateMetadataPayload(streams));
        return stream.ToArray();
    }

    public static V3StreamSpec TravelStream() => new(
        SstV5ProtocolConstants.StreamTravel,
        SstV5ProtocolConstants.TimingFixedRate,
        SstV5ProtocolConstants.SensorForkTravel | SstV5ProtocolConstants.SensorShockTravel,
        0,
        200_000,
        50,
        4,
        [
            V3SourceSpec.Travel(SstV5ProtocolConstants.SensorForkTravel, 0),
            V3SourceSpec.Travel(SstV5ProtocolConstants.SensorShockTravel, 2)
        ]);

    public static V3StreamSpec ImuStream() => new(
        SstV5ProtocolConstants.StreamImu,
        SstV5ProtocolConstants.TimingFixedRate,
        SstV5ProtocolConstants.SensorFrameImu | SstV5ProtocolConstants.SensorForkImu,
        0,
        100_000,
        50,
        24,
        [
            V3SourceSpec.Imu(SstV5ProtocolConstants.SensorFrameImu, 0),
            V3SourceSpec.Imu(SstV5ProtocolConstants.SensorForkImu, 12)
        ]);

    public static V3StreamSpec GpsStream() => new(
        SstV5ProtocolConstants.StreamGps,
        SstV5ProtocolConstants.TimingGpsReceiverTimed,
        SstV5ProtocolConstants.SensorGps,
        0,
        5_000,
        0,
        SstV5ProtocolConstants.M8NRecordSize,
        [],
        GpsDriverId: SstV5ProtocolConstants.GpsDriverM8N,
        GpsPayloadEncodingId: SstV5ProtocolConstants.EncodingGpsNavFixV1,
        GpsPayloadRecordBytes: SstV5ProtocolConstants.M8NRecordSize);

    public static V3StreamSpec BatteryStream() => new(
        SstV5ProtocolConstants.StreamBattery,
        SstV5ProtocolConstants.TimingMonotonicEventStatus,
        SstV5ProtocolConstants.SensorBattery,
        0,
        30,
        0,
        4,
        []);

    public static V3StreamSpec MarkerStream() => new(
        SstV5ProtocolConstants.StreamMarker,
        SstV5ProtocolConstants.TimingMonotonicEventStatus,
        0,
        0,
        0,
        0,
        1,
        []);

    public static byte[] CreateTravelDataPayload(
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        params (ushort Fork, ushort Shock)[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteDataHeader(
            writer,
            firstIndex,
            firstMonotonicDeltaUs,
            (uint)samples.Length,
            SstV5ProtocolConstants.SensorForkTravel | SstV5ProtocolConstants.SensorShockTravel);
        foreach (var sample in samples)
        {
            writer.Write(sample.Fork);
            writer.Write(sample.Shock);
        }

        return stream.ToArray();
    }

    public static byte[] CreateImuDataPayload(
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        params (ImuRecordSpec Frame, ImuRecordSpec Fork)[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteDataHeader(
            writer,
            firstIndex,
            firstMonotonicDeltaUs,
            (uint)samples.Length,
            SstV5ProtocolConstants.SensorFrameImu | SstV5ProtocolConstants.SensorForkImu);
        foreach (var sample in samples)
        {
            WriteImuRecord(writer, sample.Frame);
            WriteImuRecord(writer, sample.Fork);
        }

        return stream.ToArray();
    }

    public static byte[] CreateGpsDataPayload(ulong firstIndex, ulong firstMonotonicDeltaUs)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteDataHeader(writer, firstIndex, firstMonotonicDeltaUs, 1, SstV5ProtocolConstants.SensorGps);
        writer.Write((uint)20260701);
        writer.Write((uint)45_000);
        writer.Write((int)482_082_000);
        writer.Write((int)163_738_000);
        writer.Write((int)123_000);
        writer.Write((int)12_500);
        writer.Write((int)9_000_000);
        writer.Write((byte)3);
        writer.Write((byte)8);
        return stream.ToArray();
    }

    public static byte[] CreateBatteryDataPayload(
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        ushort millivolts,
        ushort flags)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteDataHeader(writer, firstIndex, firstMonotonicDeltaUs, 1, SstV5ProtocolConstants.SensorBattery);
        writer.Write(millivolts);
        writer.Write(flags);
        return stream.ToArray();
    }

    public static byte[] CreateMarkerDataPayload(
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        params byte[] markerTypes)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteDataHeader(writer, firstIndex, firstMonotonicDeltaUs, (uint)markerTypes.Length, 0);
        foreach (var markerType in markerTypes)
        {
            writer.Write(markerType);
        }

        return stream.ToArray();
    }

    public static byte[] CreateStatusPayload()
    {
        var payload = new byte[LiveV3ProtocolConstants.StatusHeaderSize + LiveV3ProtocolConstants.StatusRecordSize];
        payload[0] = 1;
        var offset = LiveV3ProtocolConstants.StatusHeaderSize;
        payload[offset] = SstV5ProtocolConstants.StreamTravel;
        payload[offset + 1] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(offset + 2, 2), 6);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset + 4, 8), 2);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset + 12, 8), 3);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset + 20, 8), 4);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(offset + 28, 8), 5);
        return payload;
    }

    public static byte[] CreateDeviceStatePayload()
    {
        var payload = new byte[
            LiveV3ProtocolConstants.DeviceStateHeaderSize +
            LiveV3ProtocolConstants.StreamStateRecordSize +
            LiveV3ProtocolConstants.SourceStateRecordSize];
        payload[0] = 1;
        payload[1] = 1;

        var streamOffset = LiveV3ProtocolConstants.DeviceStateHeaderSize;
        payload[streamOffset] = SstV5ProtocolConstants.StreamTravel;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(streamOffset + 4, 4), 200_000);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(streamOffset + 8, 4), 50);

        var sourceOffset = streamOffset + LiveV3ProtocolConstants.StreamStateRecordSize;
        payload[sourceOffset] = 3;
        payload[sourceOffset + 1] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(sourceOffset + 4, 4), (uint)LiveSensorInstanceMask.ForkTravel);
        return payload;
    }

    public static byte[] CreateSessionResultPayload(int streamStatusCount = 2, byte sessionResultReason = 1)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(sessionResultReason);
        writer.Write((byte)streamStatusCount);
        writer.Write((ushort)0);
        writer.Write((ulong)20_000);
        for (var index = 0; index < streamStatusCount; index++)
        {
            WriteFinalStatusRecord(writer, producerState: 1, producerFailureReason: 0, sinkBacklogBatches: 7);
        }

        return stream.ToArray();
    }

    public static uint ReadUInt32(ReadOnlySpan<byte> payload, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(offset, sizeof(uint)));

    public static ushort ReadUInt16(ReadOnlySpan<byte> payload, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(offset, sizeof(ushort)));

    private static byte[] CreateMetadataPayload(params V3StreamSpec[] streams)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var orderedStreams = streams.OrderBy(spec => spec.Kind).ToArray();
        var sourceCount = orderedStreams.Sum(spec => spec.Sources.Length);
        var streamMask = orderedStreams.Aggregate(0u, (mask, spec) => mask | SstV5ProtocolConstants.StreamMaskForKind(spec.Kind));

        writer.Write((byte)2);
        writer.Write((byte)orderedStreams.Length);
        writer.Write((byte)sourceCount);
        writer.Write((byte)0);
        writer.Write(streamMask);
        writer.Write((ushort)0);
        writer.Write((ushort)0);

        foreach (var spec in orderedStreams)
        {
            WriteStreamDescriptor(writer, spec);
        }

        foreach (var spec in orderedStreams)
        {
            foreach (var source in spec.Sources.OrderBy(source => source.SourceBitMask))
            {
                WriteSourceDescriptor(writer, source);
            }
        }

        return stream.ToArray();
    }

    private static void WriteStreamDescriptor(BinaryWriter writer, V3StreamSpec spec)
    {
        writer.Write(spec.Kind);
        writer.Write(spec.TimingModelId);
        writer.Write((byte)spec.Sources.Length);
        writer.Write((byte)0);
        writer.Write(spec.AcceptedSensorMask);
        writer.Write(spec.AcceptedExtensionMask);
        writer.Write(spec.AcceptedRateMhz);
        writer.Write(spec.AcceptedBatchDurationMs);
        writer.Write(spec.CompactPayloadRecordBytes);
        writer.Write((ushort)0);

        if (spec.Kind == SstV5ProtocolConstants.StreamGps)
        {
            writer.Write(spec.GpsDriverId);
            writer.Write(spec.GpsPayloadEncodingId);
            writer.Write(spec.GpsPayloadRecordBytes);
            writer.Write(spec.GpsExtensionRecordBytes);
            writer.Write((ushort)0);
        }
    }

    private static void WriteSourceDescriptor(BinaryWriter writer, V3SourceSpec source)
    {
        writer.Write(source.StreamKind);
        writer.Write(source.DriverId);
        writer.Write(source.PayloadEncodingId);
        writer.Write(source.PayloadValueCount);
        writer.Write(source.SourceBitMask);
        writer.Write(source.PayloadByteOffset);
        writer.Write(source.PayloadRecordBytes);
        writer.Write(source.PayloadValueWidthBits);
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
                writer.Write(source.AccelLsbPerG);
                writer.Write(source.GyroLsbPerDps);
                writer.Write((byte)1);
                writer.Write(new byte[3]);
                break;
        }
    }

    private static void WriteFinalStatusRecord(
        BinaryWriter writer,
        byte producerState,
        byte producerFailureReason,
        ushort sinkBacklogBatches)
    {
        writer.Write(producerState);
        writer.Write(producerFailureReason);
        writer.Write(sinkBacklogBatches);
        writer.Write((ulong)0);
        writer.Write((ulong)0);
        writer.Write((ulong)0);
        writer.Write((ulong)0);
    }

    private static void WriteDataHeader(
        BinaryWriter writer,
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        uint sampleCount,
        uint validityMask)
    {
        writer.Write(firstIndex);
        writer.Write(firstMonotonicDeltaUs);
        writer.Write(sampleCount);
        writer.Write(validityMask);
        writer.Write((ushort)0);
    }

    private static void WriteImuRecord(BinaryWriter writer, ImuRecordSpec record)
    {
        writer.Write(record.Ax);
        writer.Write(record.Ay);
        writer.Write(record.Az);
        writer.Write(record.Gx);
        writer.Write(record.Gy);
        writer.Write(record.Gz);
    }
}

internal readonly record struct V3StreamSpec(
    byte Kind,
    byte TimingModelId,
    uint AcceptedSensorMask,
    uint AcceptedExtensionMask,
    uint AcceptedRateMhz,
    uint AcceptedBatchDurationMs,
    ushort CompactPayloadRecordBytes,
    V3SourceSpec[] Sources,
    byte GpsDriverId = 0,
    byte GpsPayloadEncodingId = 0,
    ushort GpsPayloadRecordBytes = 0,
    ushort GpsExtensionRecordBytes = 0);

internal readonly record struct V3SourceSpec(
    byte StreamKind,
    byte DriverId,
    byte PayloadEncodingId,
    byte PayloadValueCount,
    uint SourceBitMask,
    ushort PayloadByteOffset,
    ushort PayloadRecordBytes,
    ushort PayloadValueWidthBits,
    float AccelLsbPerG = 4096.0f,
    float GyroLsbPerDps = 32.8f)
{
    public static V3SourceSpec Travel(uint sourceBitMask, ushort offset) =>
        new(SstV5ProtocolConstants.StreamTravel, 1, SstV5ProtocolConstants.EncodingTravelAdjustedU16, 1, sourceBitMask, offset, 2, 16);

    public static V3SourceSpec Imu(uint sourceBitMask, ushort offset) =>
        new(SstV5ProtocolConstants.StreamImu, 1, SstV5ProtocolConstants.EncodingImuI16X6CalCounts, 6, sourceBitMask, offset, 12, 16);
}

internal readonly record struct ImuRecordSpec(short Ax, short Ay, short Az, short Gx, short Gy, short Gz);

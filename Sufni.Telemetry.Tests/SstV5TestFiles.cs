using System.Text;

namespace Sufni.Telemetry.Tests;

internal static class SstV5TestFiles
{
    public const byte StreamTravel = 1;
    public const byte StreamImu = 2;
    public const byte StreamTemperature = 3;
    public const byte StreamGps = 4;
    public const byte StreamBattery = 5;
    public const byte StreamMarker = 6;

    public const ushort ChunkSessionMetadata = 1;
    public const ushort ChunkTravelData = 2;
    public const ushort ChunkImuData = 3;
    public const ushort ChunkTemperatureData = 4;
    public const ushort ChunkGpsData = 5;
    public const ushort ChunkBatteryData = 6;
    public const ushort ChunkMarkerData = 7;
    public const ushort ChunkFinalStatus = 8;

    public const uint ForkTravel = 0x00000001;
    public const uint ShockTravel = 0x00000002;
    public const uint FrameImu = 0x00000004;
    public const uint ForkImu = 0x00000008;
    public const uint RearImu = 0x00000010;
    public const uint Gps = 0x00000020;
    public const uint Battery = 0x00000040;
    public const uint GpsDiagnostics = 0x00000001;

    public const uint TravelRate200Hz = 200_000;
    public const uint ImuRate100Hz = 100_000;

    public static MemoryStream CreateStream(long sessionStartUtcMs = 1_700_000_000_123, params Action<BinaryWriter>[] chunks) =>
        new(Create(sessionStartUtcMs, chunks));

    public static byte[] Create(long sessionStartUtcMs = 1_700_000_000_123, params Action<BinaryWriter>[] chunks)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("SST5"));
        writer.Write((ushort)24);
        writer.Write((ushort)0);
        writer.Write(sessionStartUtcMs);
        writer.Write((ulong)0);

        foreach (var chunk in chunks)
        {
            chunk(writer);
        }

        return stream.ToArray();
    }

    public static Action<BinaryWriter> Metadata(params V5StreamSpec[] streams)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                var orderedStreams = streams.OrderBy(spec => spec.Kind).ToArray();
                var sourceCount = orderedStreams.Sum(spec => spec.Sources.Length);
                var streamMask = orderedStreams.Aggregate(0u, (mask, spec) => mask | (1u << spec.Kind));

                payload.Write((byte)2);
                payload.Write((byte)orderedStreams.Length);
                payload.Write((byte)sourceCount);
                payload.Write((byte)0);
                payload.Write(streamMask);
                payload.Write((ushort)0);
                payload.Write((ushort)0);

                foreach (var spec in orderedStreams)
                {
                    WriteStreamDescriptor(payload, spec);
                }

                foreach (var spec in orderedStreams)
                {
                    foreach (var source in spec.Sources.OrderBy(source => source.SourceBitMask))
                    {
                        WriteSourceDescriptor(payload, source);
                    }
                }
            }

            Chunk(ChunkSessionMetadata, stream.ToArray())(writer);
        };
    }

    public static V5StreamSpec TravelStream(uint rateMhz = TravelRate200Hz, uint sensors = ForkTravel | ShockTravel)
    {
        var sources = new List<V5SourceSpec>();
        ushort offset = 0;
        if ((sensors & ForkTravel) != 0)
        {
            sources.Add(V5SourceSpec.Travel(ForkTravel, offset));
            offset += 2;
        }

        if ((sensors & ShockTravel) != 0)
        {
            sources.Add(V5SourceSpec.Travel(ShockTravel, offset));
            offset += 2;
        }

        return new V5StreamSpec(StreamTravel, 1, sensors, 0, rateMhz, 50, offset, [.. sources]);
    }

    public static V5StreamSpec ImuStream(uint rateMhz = ImuRate100Hz, uint sensors = FrameImu | ForkImu)
    {
        var sources = new List<V5SourceSpec>();
        ushort offset = 0;
        foreach (var mask in new[] { FrameImu, ForkImu, RearImu })
        {
            if ((sensors & mask) == 0)
            {
                continue;
            }

            sources.Add(V5SourceSpec.Imu(mask, offset));
            offset += 12;
        }

        return new V5StreamSpec(StreamImu, 1, sensors, 0, rateMhz, 50, offset, [.. sources]);
    }

    public static V5StreamSpec TemperatureStream(uint sensors = FrameImu)
    {
        var sources = new List<V5SourceSpec>();
        ushort offset = 0;
        foreach (var mask in new[] { FrameImu, ForkImu, RearImu })
        {
            if ((sensors & mask) == 0)
            {
                continue;
            }

            sources.Add(V5SourceSpec.Temperature(mask, offset));
            offset += 2;
        }

        return new V5StreamSpec(StreamTemperature, 2, sensors, 0, 30, 0, offset, [.. sources]);
    }

    public static V5StreamSpec GpsStream(byte driver = 2, bool diagnostics = false)
    {
        var payloadBytes = driver == 1 ? (ushort)38 : (ushort)30;
        var extensionBytes = diagnostics ? (ushort)17 : (ushort)0;
        return new V5StreamSpec(
            StreamGps,
            3,
            Gps,
            diagnostics ? GpsDiagnostics : 0,
            5_000,
            0,
            (ushort)(payloadBytes + extensionBytes),
            [],
            GpsDriverId: driver,
            GpsPayloadEncodingId: 4,
            GpsPayloadRecordBytes: payloadBytes,
            GpsExtensionRecordBytes: extensionBytes);
    }

    public static V5StreamSpec BatteryStream() =>
        new(StreamBattery, 2, Battery, 0, 30, 0, 4, []);

    public static V5StreamSpec MarkerStream() =>
        new(StreamMarker, 2, 0, 0, 0, 0, 1, []);

    public static Action<BinaryWriter> TravelData(
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        uint validityMask,
        params (ushort Fork, ushort Shock)[] samples)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteDataHeader(payload, firstIndex, firstMonotonicDeltaUs, (uint)samples.Length, validityMask);
                foreach (var (fork, shock) in samples)
                {
                    payload.Write(fork);
                    payload.Write(shock);
                }
            }

            Chunk(ChunkTravelData, stream.ToArray())(writer);
        };
    }

    public static Action<BinaryWriter> ImuData(
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        uint validityMask,
        params (ImuRecordSpec Frame, ImuRecordSpec Fork)[] samples)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteDataHeader(payload, firstIndex, firstMonotonicDeltaUs, (uint)samples.Length, validityMask);
                foreach (var (frame, fork) in samples)
                {
                    WriteImuRecord(payload, frame);
                    WriteImuRecord(payload, fork);
                }
            }

            Chunk(ChunkImuData, stream.ToArray())(writer);
        };
    }

    public static Action<BinaryWriter> TemperatureData(
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        uint validityMask,
        params short[] rawCounts)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteDataHeader(payload, firstIndex, firstMonotonicDeltaUs, (uint)rawCounts.Length, validityMask);
                foreach (var raw in rawCounts)
                {
                    payload.Write(raw);
                }
            }

            Chunk(ChunkTemperatureData, stream.ToArray())(writer);
        };
    }

    public static Action<BinaryWriter> GpsM8NData(
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        bool diagnostics,
        params V5GpsM8NRecord[] records)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteDataHeader(payload, firstIndex, firstMonotonicDeltaUs, (uint)records.Length, Gps);
                foreach (var record in records)
                {
                    payload.Write(record.Date);
                    payload.Write(record.TimeMs);
                    payload.Write(record.LatitudeE7);
                    payload.Write(record.LongitudeE7);
                    payload.Write(record.AltitudeMm);
                    payload.Write(record.SpeedMmPerSecond);
                    payload.Write(record.HeadingE5);
                    payload.Write(record.FixMode);
                    payload.Write(record.Satellites);
                    if (diagnostics)
                    {
                        payload.Write(record.Quality);
                        payload.Write(record.Hdop);
                        payload.Write(record.Pdop);
                        payload.Write(record.Epe2d);
                        payload.Write(record.Epe3d);
                    }
                }
            }

            Chunk(ChunkGpsData, stream.ToArray())(writer);
        };
    }

    public static Action<BinaryWriter> MarkerData(ulong firstIndex, ulong firstMonotonicDeltaUs, params byte[] markerTypes)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteDataHeader(payload, firstIndex, firstMonotonicDeltaUs, (uint)markerTypes.Length, 0);
                foreach (var markerType in markerTypes)
                {
                    payload.Write(markerType);
                }
            }

            Chunk(ChunkMarkerData, stream.ToArray())(writer);
        };
    }

    public static Action<BinaryWriter> BatteryData(ulong firstIndex, ulong firstMonotonicDeltaUs, params (ushort Millivolts, ushort Flags)[] records)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                WriteDataHeader(payload, firstIndex, firstMonotonicDeltaUs, (uint)records.Length, Battery);
                foreach (var (millivolts, flags) in records)
                {
                    payload.Write(millivolts);
                    payload.Write(flags);
                }
            }

            Chunk(ChunkBatteryData, stream.ToArray())(writer);
        };
    }

    public static Action<BinaryWriter> FinalStatus(params V5FinalStreamStatus[] statuses) =>
        FinalStatus(1, statuses);

    public static Action<BinaryWriter> FinalStatus(byte sessionResultReason, params V5FinalStreamStatus[] statuses)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                payload.Write(sessionResultReason);
                payload.Write((byte)statuses.Length);
                payload.Write((ushort)0);
                payload.Write((ulong)20_000);
                foreach (var status in statuses)
                {
                    payload.Write(status.ProducerState);
                    payload.Write(status.ProducerFailureReason);
                    payload.Write((ushort)0);
                    payload.Write(status.ProducerMissedCount);
                    payload.Write(status.ProducerMissingTimeUs);
                    payload.Write(status.SinkMissedCount);
                    payload.Write(status.SinkMissingTimeUs);
                }
            }

            Chunk(ChunkFinalStatus, stream.ToArray())(writer);
        };
    }

    public static V5FinalStreamStatus OkStatus() => new(1, 0, 0, 0, 0, 0);

    public static Action<BinaryWriter> Chunk(ushort type, byte[] payload, uint? declaredLength = null, ushort flags = 0)
    {
        return writer =>
        {
            writer.Write(type);
            writer.Write(flags);
            writer.Write(declaredLength ?? (uint)payload.Length);
            writer.Write(payload);
        };
    }

    private static void WriteStreamDescriptor(BinaryWriter writer, V5StreamSpec spec)
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

        if (spec.Kind == StreamGps)
        {
            writer.Write(spec.GpsDriverId);
            writer.Write(spec.GpsPayloadEncodingId);
            writer.Write(spec.GpsPayloadRecordBytes);
            writer.Write(spec.GpsExtensionRecordBytes);
            writer.Write((ushort)0);
        }
    }

    private static void WriteSourceDescriptor(BinaryWriter writer, V5SourceSpec source)
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
            case StreamTravel:
                writer.Write((ushort)0);
                writer.Write((ushort)4095);
                writer.Write((ushort)4096);
                writer.Write((byte)1);
                writer.Write((byte)0);
                break;
            case StreamImu:
                writer.Write(source.AccelLsbPerG);
                writer.Write(source.GyroLsbPerDps);
                writer.Write((byte)1);
                writer.Write(new byte[3]);
                break;
            case StreamTemperature:
                writer.Write(source.TemperatureLsbPerCelsius);
                writer.Write(source.TemperatureCelsiusAtRawZero);
                break;
        }
    }

    private static void WriteDataHeader(BinaryWriter writer, ulong firstIndex, ulong firstMonotonicDeltaUs, uint sampleCount, uint validityMask)
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

internal readonly record struct V5StreamSpec(
    byte Kind,
    byte TimingModelId,
    uint AcceptedSensorMask,
    uint AcceptedExtensionMask,
    uint AcceptedRateMhz,
    uint AcceptedBatchDurationMs,
    ushort CompactPayloadRecordBytes,
    V5SourceSpec[] Sources,
    byte GpsDriverId = 0,
    byte GpsPayloadEncodingId = 0,
    ushort GpsPayloadRecordBytes = 0,
    ushort GpsExtensionRecordBytes = 0);

internal readonly record struct V5SourceSpec(
    byte StreamKind,
    byte DriverId,
    byte PayloadEncodingId,
    byte PayloadValueCount,
    uint SourceBitMask,
    ushort PayloadByteOffset,
    ushort PayloadRecordBytes,
    ushort PayloadValueWidthBits,
    float AccelLsbPerG = 4096.0f,
    float GyroLsbPerDps = 32.8f,
    float TemperatureLsbPerCelsius = 340.0f,
    float TemperatureCelsiusAtRawZero = 36.53f)
{
    public static V5SourceSpec Travel(uint sourceBitMask, ushort offset) =>
        new(SstV5TestFiles.StreamTravel, 1, 1, 1, sourceBitMask, offset, 2, 16);

    public static V5SourceSpec Imu(uint sourceBitMask, ushort offset) =>
        new(SstV5TestFiles.StreamImu, 1, 2, 6, sourceBitMask, offset, 12, 16);

    public static V5SourceSpec Temperature(uint sourceBitMask, ushort offset) =>
        new(SstV5TestFiles.StreamTemperature, 1, 3, 1, sourceBitMask, offset, 2, 16);
}

internal readonly record struct V5GpsM8NRecord(
    uint Date,
    uint TimeMs,
    int LatitudeE7,
    int LongitudeE7,
    int AltitudeMm = 123_000,
    int SpeedMmPerSecond = 12_500,
    int HeadingE5 = 9_000_000,
    byte FixMode = 3,
    byte Satellites = 8,
    byte Quality = 1,
    float Hdop = 0.8f,
    float Pdop = 1.2f,
    float Epe2d = 2.5f,
    float Epe3d = 3.5f);

internal readonly record struct V5FinalStreamStatus(
    byte ProducerState,
    byte ProducerFailureReason,
    ulong ProducerMissedCount,
    ulong ProducerMissingTimeUs,
    ulong SinkMissedCount,
    ulong SinkMissingTimeUs);

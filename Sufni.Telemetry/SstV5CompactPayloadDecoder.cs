using System.Buffers.Binary;

namespace Sufni.Telemetry;

public static class SstV5CompactPayloadDecoder
{
    public static IReadOnlyList<SstV5DecodedTravelRecord> DecodeTravel(
        SstV5StreamDescriptor descriptor,
        SstV5DataHeader header,
        ReadOnlySpan<byte> compactPayload)
    {
        EnsureDescriptorKind(descriptor, SstV5ProtocolConstants.StreamTravel);
        EnsureCompactPayloadLength(descriptor, header, compactPayload);

        var records = new List<SstV5DecodedTravelRecord>(CheckedCapacity(descriptor.Sources.Count, header.SampleCount));
        foreach (var source in descriptor.Sources)
        {
            if ((header.ValidityMask & source.SourceBitMask) == 0)
            {
                continue;
            }

            for (var sampleOffset = 0u; sampleOffset < header.SampleCount; sampleOffset++)
            {
                var recordOffset = CompactRecordOffset(descriptor, source, sampleOffset);
                var count = BinaryPrimitives.ReadUInt16LittleEndian(compactPayload.Slice(recordOffset, sizeof(ushort)));
                records.Add(new SstV5DecodedTravelRecord(
                    source.SourceBitMask,
                    header.FirstIndex + sampleOffset,
                    CalculateSampleMonotonicUs(header.FirstMonotonicDeltaUs, sampleOffset, descriptor.AcceptedRateMhz),
                    count));
            }
        }

        return records;
    }

    public static IReadOnlyList<SstV5DecodedImuRecord> DecodeImu(
        SstV5StreamDescriptor descriptor,
        SstV5DataHeader header,
        ReadOnlySpan<byte> compactPayload)
    {
        EnsureDescriptorKind(descriptor, SstV5ProtocolConstants.StreamImu);
        EnsureCompactPayloadLength(descriptor, header, compactPayload);

        var records = new List<SstV5DecodedImuRecord>(CheckedCapacity(descriptor.Sources.Count, header.SampleCount));
        foreach (var source in descriptor.Sources)
        {
            if ((header.ValidityMask & source.SourceBitMask) == 0)
            {
                continue;
            }

            var locationId = SstV5ProtocolConstants.GetImuLocationId(source.SourceBitMask);
            for (var sampleOffset = 0u; sampleOffset < header.SampleCount; sampleOffset++)
            {
                var recordOffset = CompactRecordOffset(descriptor, source, sampleOffset);
                records.Add(new SstV5DecodedImuRecord(
                    source.SourceBitMask,
                    locationId,
                    header.FirstIndex + sampleOffset,
                    CalculateSampleMonotonicUs(header.FirstMonotonicDeltaUs, sampleOffset, descriptor.AcceptedRateMhz),
                    ReadImuRecord(compactPayload.Slice(recordOffset, source.PayloadRecordBytes))));
            }
        }

        return records;
    }

    public static IReadOnlyList<SstV5DecodedTemperatureRecord> DecodeTemperature(
        SstV5StreamDescriptor descriptor,
        SstV5DataHeader header,
        long sessionStartUtcMs,
        ReadOnlySpan<byte> compactPayload)
    {
        EnsureDescriptorKind(descriptor, SstV5ProtocolConstants.StreamTemperature);
        EnsureCompactPayloadLength(descriptor, header, compactPayload);

        var records = new List<SstV5DecodedTemperatureRecord>(CheckedCapacity(descriptor.Sources.Count, header.SampleCount));
        foreach (var source in descriptor.Sources)
        {
            if ((header.ValidityMask & source.SourceBitMask) == 0)
            {
                continue;
            }

            var locationId = SstV5ProtocolConstants.GetImuLocationId(source.SourceBitMask);
            for (var sampleOffset = 0u; sampleOffset < header.SampleCount; sampleOffset++)
            {
                var recordOffset = CompactRecordOffset(descriptor, source, sampleOffset);
                var raw = BinaryPrimitives.ReadInt16LittleEndian(compactPayload.Slice(recordOffset, sizeof(short)));
                var celsius = raw / source.TemperatureLsbPerCelsius + source.TemperatureCelsiusAtRawZero;
                var monotonicDeltaUs = descriptor.AcceptedRateMhz > 0
                    ? CalculateSampleMonotonicUs(header.FirstMonotonicDeltaUs, sampleOffset, descriptor.AcceptedRateMhz)
                    : header.FirstMonotonicDeltaUs;
                var timestampUtc = checked((sessionStartUtcMs * 1000 + (long)monotonicDeltaUs) / 1_000_000);
                records.Add(new SstV5DecodedTemperatureRecord(
                    source.SourceBitMask,
                    locationId,
                    header.FirstIndex + sampleOffset,
                    monotonicDeltaUs,
                    new TemperatureSample(timestampUtc, locationId, celsius)));
            }
        }

        return records;
    }

    public static IReadOnlyList<SstV5DecodedGpsRecord> DecodeGps(
        SstV5StreamDescriptor descriptor,
        SstV5DataHeader header,
        ReadOnlySpan<byte> compactPayload)
    {
        EnsureDescriptorKind(descriptor, SstV5ProtocolConstants.StreamGps);
        EnsureCompactPayloadLength(descriptor, header, compactPayload);

        if ((header.ValidityMask & SstV5ProtocolConstants.SensorGps) == 0)
        {
            return [];
        }

        var records = new List<SstV5DecodedGpsRecord>(checked((int)header.SampleCount));
        for (var sampleOffset = 0u; sampleOffset < header.SampleCount; sampleOffset++)
        {
            var recordOffset = CompactRecordOffset(descriptor, sampleOffset);
            var record = DecodeGpsRecord(descriptor, compactPayload.Slice(recordOffset, descriptor.CompactPayloadRecordBytes));
            if (record is not null)
            {
                records.Add(new SstV5DecodedGpsRecord(
                    header.FirstIndex + sampleOffset,
                    header.FirstMonotonicDeltaUs,
                    record));
            }
        }

        return records;
    }

    public static IReadOnlyList<SstV5DecodedBatteryRecord> DecodeBattery(
        SstV5StreamDescriptor descriptor,
        SstV5DataHeader header,
        ReadOnlySpan<byte> compactPayload)
    {
        EnsureDescriptorKind(descriptor, SstV5ProtocolConstants.StreamBattery);
        EnsureCompactPayloadLength(descriptor, header, compactPayload);

        if ((header.ValidityMask & SstV5ProtocolConstants.SensorBattery) == 0)
        {
            return [];
        }

        var records = new SstV5DecodedBatteryRecord[checked((int)header.SampleCount)];
        for (var sampleOffset = 0u; sampleOffset < header.SampleCount; sampleOffset++)
        {
            var recordOffset = CompactRecordOffset(descriptor, sampleOffset);
            records[checked((int)sampleOffset)] = new SstV5DecodedBatteryRecord(
                header.FirstIndex + sampleOffset,
                header.FirstMonotonicDeltaUs,
                BinaryPrimitives.ReadUInt16LittleEndian(compactPayload.Slice(recordOffset, sizeof(ushort))),
                BinaryPrimitives.ReadUInt16LittleEndian(compactPayload.Slice(recordOffset + sizeof(ushort), sizeof(ushort))));
        }

        return records;
    }

    public static IReadOnlyList<SstV5DecodedMarkerRecord> DecodeMarkers(
        SstV5StreamDescriptor descriptor,
        SstV5DataHeader header,
        ReadOnlySpan<byte> compactPayload)
    {
        EnsureDescriptorKind(descriptor, SstV5ProtocolConstants.StreamMarker);
        EnsureCompactPayloadLength(descriptor, header, compactPayload);

        return
        [
            new SstV5DecodedMarkerRecord(
                header.FirstIndex,
                header.FirstMonotonicDeltaUs,
                compactPayload[0])
        ];
    }

    public static ulong CalculateSampleMonotonicUs(ulong firstMonotonicDeltaUs, ulong sampleOffset, uint rateMhz) =>
        checked(firstMonotonicDeltaUs + RoundDurationUs(sampleOffset, rateMhz));

    public static ulong RoundDurationUs(ulong sampleCount, uint rateMhz)
    {
        if (sampleCount == 0 || rateMhz == 0)
        {
            return 0;
        }

        return checked((ulong)Math.Round(sampleCount * 1_000_000_000.0 / rateMhz, MidpointRounding.AwayFromZero));
    }

    private static GpsRecord? DecodeGpsRecord(SstV5StreamDescriptor descriptor, ReadOnlySpan<byte> recordBytes)
    {
        var reader = new SstByteReader(recordBytes);
        var date = reader.ReadUInt32();
        var timeMs = reader.ReadUInt32();
        double latitude;
        double longitude;
        float altitude;
        float speed;
        float heading;
        byte fixMode;
        byte satellites;

        if (descriptor.GpsDriverId == SstV5ProtocolConstants.GpsDriverLc76G)
        {
            latitude = reader.ReadDouble();
            longitude = reader.ReadDouble();
            altitude = reader.ReadSingle();
            speed = reader.ReadSingle();
            heading = reader.ReadSingle();
            fixMode = reader.ReadByte();
            satellites = reader.ReadByte();
        }
        else
        {
            latitude = reader.ReadInt32() / 10_000_000.0;
            longitude = reader.ReadInt32() / 10_000_000.0;
            altitude = reader.ReadInt32() / 1000.0f;
            speed = reader.ReadInt32() / 1000.0f;
            heading = reader.ReadInt32() / 100_000.0f;
            fixMode = reader.ReadByte();
            satellites = reader.ReadByte();
        }

        if (fixMode is not 0 and not 2 and not 3)
        {
            return null;
        }

        var epe2d = float.NaN;
        var epe3d = float.NaN;
        if (descriptor.GpsExtensionRecordBytes > 0)
        {
            _ = reader.ReadByte(); // quality
            _ = reader.ReadSingle(); // hdop
            _ = reader.ReadSingle(); // pdop
            epe2d = reader.ReadSingle();
            epe3d = reader.ReadSingle();
        }

        if (!TryCreateGpsTimestamp(date, timeMs, out var timestamp))
        {
            return null;
        }

        return new GpsRecord(timestamp, latitude, longitude, altitude, speed, heading, fixMode, satellites, epe2d, epe3d);
    }

    private static bool TryCreateGpsTimestamp(uint date, uint timeMs, out DateTime timestamp)
    {
        timestamp = default;
        if (date == 0)
        {
            return false;
        }

        var year = (int)(date / 10000);
        var month = (int)(date / 100 % 100);
        var day = (int)(date % 100);

        if (year is < 1 or > 9999 || month is < 1 or > 12)
        {
            return false;
        }

        if (day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        timestamp = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(timeMs);
        return true;
    }

    private static ImuRecord ReadImuRecord(ReadOnlySpan<byte> bytes)
    {
        var reader = new SstByteReader(bytes);
        return new ImuRecord(
            reader.ReadInt16(),
            reader.ReadInt16(),
            reader.ReadInt16(),
            reader.ReadInt16(),
            reader.ReadInt16(),
            reader.ReadInt16());
    }

    private static void EnsureDescriptorKind(SstV5StreamDescriptor descriptor, byte expectedKind)
    {
        if (descriptor.StreamKind != expectedKind)
        {
            throw new ArgumentException("SST v5 descriptor stream kind does not match the requested decoder.", nameof(descriptor));
        }
    }

    private static void EnsureCompactPayloadLength(
        SstV5StreamDescriptor descriptor,
        SstV5DataHeader header,
        ReadOnlySpan<byte> compactPayload)
    {
        var expectedLength = checked((int)(header.SampleCount * descriptor.CompactPayloadRecordBytes));
        if (compactPayload.Length != expectedLength)
        {
            throw new FormatException("SST v5 compact payload length is invalid.");
        }
    }

    private static int CheckedCapacity(int sourceCount, uint sampleCount) =>
        checked(sourceCount * (int)sampleCount);

    private static int CompactRecordOffset(
        SstV5StreamDescriptor descriptor,
        SstV5SourceDescriptor source,
        uint sampleOffset) =>
        checked((int)(sampleOffset * descriptor.CompactPayloadRecordBytes + source.PayloadByteOffset));

    private static int CompactRecordOffset(SstV5StreamDescriptor descriptor, uint sampleOffset) =>
        checked((int)(sampleOffset * descriptor.CompactPayloadRecordBytes));
}

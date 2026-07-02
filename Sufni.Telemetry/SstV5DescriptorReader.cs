namespace Sufni.Telemetry;

public static class SstV5DescriptorReader
{
    public static SstV5SessionDescriptor ReadSessionDescriptor(ReadOnlySpan<byte> payload)
    {
        var reader = new SstByteReader(payload);
        if (payload.Length < SstV5ProtocolConstants.MetadataHeaderSize)
        {
            throw new FormatException("SST v5 metadata payload is truncated.");
        }

        var boardId = reader.ReadByte();
        var streamDescriptorCount = reader.ReadByte();
        var sourceDescriptorTotalCount = reader.ReadByte();
        var omissionCount = reader.ReadByte();
        var acceptedStreamMask = reader.ReadUInt32();
        var metadataFlags = reader.ReadUInt16();
        var reserved = reader.ReadUInt16();

        if (boardId is not 1 and not 2)
        {
            throw new FormatException("SST v5 board ID is invalid.");
        }

        if (metadataFlags != 0 || reserved != 0)
        {
            throw new FormatException("SST v5 metadata flags are invalid.");
        }

        var streamBuilders = new List<StreamDescriptorBuilder>(streamDescriptorCount);
        var streamBuilderByKind = new Dictionary<byte, StreamDescriptorBuilder>();
        byte previousStreamKind = 0;
        uint emittedStreamMask = 0;
        for (var index = 0; index < streamDescriptorCount; index++)
        {
            var descriptor = ReadStreamDescriptor(ref reader);
            if (descriptor.StreamKind <= previousStreamKind)
            {
                throw new FormatException("SST v5 stream descriptors are out of order.");
            }

            previousStreamKind = descriptor.StreamKind;
            emittedStreamMask |= SstV5ProtocolConstants.StreamMaskForKind(descriptor.StreamKind);
            streamBuilders.Add(descriptor);
            streamBuilderByKind.Add(descriptor.StreamKind, descriptor);
        }

        if (emittedStreamMask != acceptedStreamMask)
        {
            throw new FormatException("SST v5 accepted stream mask does not match descriptors.");
        }

        byte previousSourceStreamKind = 0;
        uint previousSourceMask = 0;
        for (var index = 0; index < sourceDescriptorTotalCount; index++)
        {
            var source = ReadSourceDescriptor(ref reader, streamBuilderByKind);
            if (source.StreamKind < previousSourceStreamKind ||
                (source.StreamKind == previousSourceStreamKind && source.SourceBitMask <= previousSourceMask))
            {
                throw new FormatException("SST v5 source descriptors are out of order.");
            }

            previousSourceStreamKind = source.StreamKind;
            previousSourceMask = source.SourceBitMask;
            streamBuilderByKind[source.StreamKind].Sources.Add(source);
        }

        foreach (var descriptor in streamBuilders)
        {
            if (descriptor.Sources.Count != descriptor.SourceDescriptorCount)
            {
                throw new FormatException("SST v5 source descriptor count does not match stream descriptor.");
            }

            ValidateStreamDescriptorShape(descriptor);
        }

        var omissions = new SstV5OmissionRecord[omissionCount];
        for (var index = 0; index < omissionCount; index++)
        {
            if (reader.Remaining < SstV5ProtocolConstants.OmissionRecordSize)
            {
                throw new FormatException("SST v5 omission records are truncated.");
            }

            var targetKind = reader.ReadByte();
            var streamKind = reader.ReadByte();
            var admissionReason = reader.ReadByte();
            var omissionReserved = reader.ReadByte();
            var targetMask = reader.ReadUInt32();
            if (targetKind is < 1 or > 3 || omissionReserved != 0)
            {
                throw new FormatException("SST v5 omission record is invalid.");
            }

            omissions[index] = new SstV5OmissionRecord(targetKind, streamKind, admissionReason, targetMask);
        }

        if (reader.Position != reader.Length)
        {
            throw new FormatException("SST v5 metadata payload length is invalid.");
        }

        return new SstV5SessionDescriptor
        {
            BoardId = boardId,
            AcceptedStreamMask = acceptedStreamMask,
            StreamDescriptors = streamBuilders.Select(builder => builder.ToDescriptor()).ToArray(),
            OmissionRecords = omissions,
        };
    }

    public static SstV5DataHeader ReadDataHeader(
        ReadOnlySpan<byte> payload,
        SstV5StreamDescriptor descriptor)
    {
        if (payload.Length < SstV5ProtocolConstants.DataHeaderSize)
        {
            throw new FormatException("SST v5 data payload is truncated.");
        }

        var reader = new SstByteReader(payload);
        var firstIndex = reader.ReadUInt64();
        var firstMonotonicDeltaUs = reader.ReadUInt64();
        var sampleCount = reader.ReadUInt32();
        var validityMask = reader.ReadUInt32();
        var flags = reader.ReadUInt16();

        if (sampleCount == 0 || flags != 0)
        {
            throw new FormatException("SST v5 data header is invalid.");
        }

        var expectedLength = SstV5ProtocolConstants.DataHeaderSize + (ulong)sampleCount * descriptor.CompactPayloadRecordBytes;
        if (expectedLength != (ulong)payload.Length)
        {
            throw new FormatException("SST v5 data payload length is invalid.");
        }

        if (descriptor.StreamKind == SstV5ProtocolConstants.StreamMarker)
        {
            if (validityMask != 0)
            {
                throw new FormatException("SST v5 marker validity mask is invalid.");
            }

            if (sampleCount != 1)
            {
                throw new FormatException("SST v5 marker sample count is invalid.");
            }
        }
        else if ((validityMask & ~descriptor.AcceptedSensorMask) != 0)
        {
            throw new FormatException("SST v5 validity mask contains unaccepted source bits.");
        }

        return new SstV5DataHeader(
            firstIndex,
            firstMonotonicDeltaUs,
            sampleCount,
            validityMask);
    }

    private static StreamDescriptorBuilder ReadStreamDescriptor(ref SstByteReader reader)
    {
        if (reader.Remaining < SstV5ProtocolConstants.StreamDescriptorSize)
        {
            throw new FormatException("SST v5 stream descriptor is truncated.");
        }

        var descriptor = new StreamDescriptorBuilder
        {
            StreamKind = reader.ReadByte(),
            TimingModelId = reader.ReadByte(),
            SourceDescriptorCount = reader.ReadByte(),
        };
        var reserved = reader.ReadByte();
        descriptor.AcceptedSensorMask = reader.ReadUInt32();
        descriptor.AcceptedExtensionMask = reader.ReadUInt32();
        descriptor.AcceptedRateMhz = reader.ReadUInt32();
        descriptor.AcceptedBatchDurationMs = reader.ReadUInt32();
        descriptor.CompactPayloadRecordBytes = reader.ReadUInt16();
        var flags = reader.ReadUInt16();

        if (!SstV5ProtocolConstants.IsKnownStreamKind(descriptor.StreamKind) ||
            descriptor.TimingModelId is < 1 or > 3 ||
            reserved != 0 ||
            flags != 0)
        {
            throw new FormatException("SST v5 stream descriptor is invalid.");
        }

        if (descriptor.StreamKind == SstV5ProtocolConstants.StreamGps)
        {
            if (reader.Remaining < SstV5ProtocolConstants.GpsStreamDescriptorTailSize)
            {
                throw new FormatException("SST v5 GPS stream descriptor is truncated.");
            }

            descriptor.GpsDriverId = reader.ReadByte();
            descriptor.GpsPayloadEncodingId = reader.ReadByte();
            descriptor.GpsPayloadRecordBytes = reader.ReadUInt16();
            descriptor.GpsExtensionRecordBytes = reader.ReadUInt16();
            var gpsReserved = reader.ReadUInt16();
            if (gpsReserved != 0)
            {
                throw new FormatException("SST v5 GPS stream descriptor reserved field is invalid.");
            }
        }

        return descriptor;
    }

    private static SourceDescriptorBuilder ReadSourceDescriptor(
        ref SstByteReader reader,
        IReadOnlyDictionary<byte, StreamDescriptorBuilder> streamBuilderByKind)
    {
        if (reader.Remaining < SstV5ProtocolConstants.SourceDescriptorSize)
        {
            throw new FormatException("SST v5 source descriptor is truncated.");
        }

        var source = new SourceDescriptorBuilder
        {
            StreamKind = reader.ReadByte(),
            DriverId = reader.ReadByte(),
            PayloadEncodingId = reader.ReadByte(),
            PayloadValueCount = reader.ReadByte(),
            SourceBitMask = reader.ReadUInt32(),
            PayloadByteOffset = reader.ReadUInt16(),
            PayloadRecordBytes = reader.ReadUInt16(),
            PayloadValueWidthBits = reader.ReadUInt16(),
        };
        var flags = reader.ReadUInt16();

        if (flags != 0 || !streamBuilderByKind.TryGetValue(source.StreamKind, out var streamDescriptor))
        {
            throw new FormatException("SST v5 source descriptor is invalid.");
        }

        if (source.StreamKind is not SstV5ProtocolConstants.StreamTravel
            and not SstV5ProtocolConstants.StreamImu
            and not SstV5ProtocolConstants.StreamTemperature)
        {
            throw new FormatException("SST v5 source descriptor references a non-source stream.");
        }

        if (!SstV5ProtocolConstants.IsSingleBitMask(source.SourceBitMask) ||
            (source.SourceBitMask & streamDescriptor.AcceptedSensorMask) == 0)
        {
            throw new FormatException("SST v5 source descriptor source bit is invalid.");
        }

        if (!SstV5ProtocolConstants.SourceBelongsToStream(source.StreamKind, source.SourceBitMask))
        {
            throw new FormatException("SST v5 source descriptor source bit does not belong to the stream.");
        }

        if (source.PayloadByteOffset + source.PayloadRecordBytes > streamDescriptor.CompactPayloadRecordBytes)
        {
            throw new FormatException("SST v5 source descriptor payload range is invalid.");
        }

        switch (source.StreamKind)
        {
            case SstV5ProtocolConstants.StreamTravel:
                ReadTravelSourceTail(ref reader, source);
                break;
            case SstV5ProtocolConstants.StreamImu:
                ReadImuSourceTail(ref reader, source);
                break;
            case SstV5ProtocolConstants.StreamTemperature:
                ReadTemperatureSourceTail(ref reader, source);
                break;
        }

        return source;
    }

    private static void ReadTravelSourceTail(ref SstByteReader reader, SourceDescriptorBuilder source)
    {
        if (reader.Remaining < SstV5ProtocolConstants.TravelSourceDescriptorTailSize)
        {
            throw new FormatException("SST v5 travel source descriptor is truncated.");
        }

        source.CountMin = reader.ReadUInt16();
        source.CountMax = reader.ReadUInt16();
        source.WrapModulus = reader.ReadUInt16();
        source.Wraps = reader.ReadByte();
        var reserved = reader.ReadByte();

        if (reserved != 0 ||
            source.PayloadEncodingId != SstV5ProtocolConstants.EncodingTravelAdjustedU16 ||
            source.PayloadRecordBytes != 2 ||
            source.PayloadValueWidthBits != 16 ||
            source.PayloadValueCount != 1 ||
            source.Wraps is not 0 and not 1)
        {
            throw new FormatException("SST v5 travel source descriptor is invalid.");
        }
    }

    private static void ReadImuSourceTail(ref SstByteReader reader, SourceDescriptorBuilder source)
    {
        if (reader.Remaining < SstV5ProtocolConstants.ImuSourceDescriptorTailSize)
        {
            throw new FormatException("SST v5 IMU source descriptor is truncated.");
        }

        source.AccelLsbPerG = reader.ReadSingle();
        source.GyroLsbPerDps = reader.ReadSingle();
        source.BikeFrameId = reader.ReadByte();
        var reserved = reader.ReadBytes(3);

        if (reserved[0] != 0 ||
            reserved[1] != 0 ||
            reserved[2] != 0 ||
            source.PayloadEncodingId != SstV5ProtocolConstants.EncodingImuI16X6CalCounts ||
            source.PayloadRecordBytes != 12 ||
            source.PayloadValueCount != 6 ||
            source.BikeFrameId != 1)
        {
            throw new FormatException("SST v5 IMU source descriptor is invalid.");
        }
    }

    private static void ReadTemperatureSourceTail(ref SstByteReader reader, SourceDescriptorBuilder source)
    {
        if (reader.Remaining < SstV5ProtocolConstants.TemperatureSourceDescriptorTailSize)
        {
            throw new FormatException("SST v5 temperature source descriptor is truncated.");
        }

        source.TemperatureLsbPerCelsius = reader.ReadSingle();
        source.TemperatureCelsiusAtRawZero = reader.ReadSingle();

        if (source.PayloadEncodingId != SstV5ProtocolConstants.EncodingTemperatureRawI16 ||
            source.PayloadRecordBytes != 2 ||
            source.PayloadValueCount != 1 ||
            source.PayloadValueWidthBits != 16 ||
            source.TemperatureLsbPerCelsius == 0)
        {
            throw new FormatException("SST v5 temperature source descriptor is invalid.");
        }
    }

    private static void ValidateStreamDescriptorShape(StreamDescriptorBuilder descriptor)
    {
        switch (descriptor.StreamKind)
        {
            case SstV5ProtocolConstants.StreamTravel:
                ValidateFixedSourceStream(descriptor, SstV5ProtocolConstants.TimingFixedRate);
                if ((descriptor.AcceptedSensorMask &
                     ~(SstV5ProtocolConstants.SensorForkTravel | SstV5ProtocolConstants.SensorShockTravel)) != 0)
                {
                    throw new FormatException("SST v5 travel descriptor accepted sensor mask is invalid.");
                }

                break;
            case SstV5ProtocolConstants.StreamImu:
                ValidateFixedSourceStream(descriptor, SstV5ProtocolConstants.TimingFixedRate);
                if ((descriptor.AcceptedSensorMask &
                     ~(SstV5ProtocolConstants.SensorFrameImu |
                       SstV5ProtocolConstants.SensorForkImu |
                       SstV5ProtocolConstants.SensorRearImu)) != 0)
                {
                    throw new FormatException("SST v5 IMU descriptor accepted sensor mask is invalid.");
                }

                break;
            case SstV5ProtocolConstants.StreamTemperature:
                if (descriptor.TimingModelId != SstV5ProtocolConstants.TimingMonotonicEventStatus ||
                    (descriptor.AcceptedSensorMask &
                     ~(SstV5ProtocolConstants.SensorFrameImu |
                       SstV5ProtocolConstants.SensorForkImu |
                       SstV5ProtocolConstants.SensorRearImu)) != 0)
                {
                    throw new FormatException("SST v5 temperature descriptor is invalid.");
                }

                break;
            case SstV5ProtocolConstants.StreamGps:
                ValidateGpsStreamDescriptor(descriptor);
                break;
            case SstV5ProtocolConstants.StreamBattery:
                if (descriptor.SourceDescriptorCount != 0 ||
                    descriptor.TimingModelId != SstV5ProtocolConstants.TimingMonotonicEventStatus ||
                    descriptor.CompactPayloadRecordBytes != 4 ||
                    descriptor.AcceptedSensorMask != SstV5ProtocolConstants.SensorBattery ||
                    descriptor.AcceptedExtensionMask != 0)
                {
                    throw new FormatException("SST v5 battery descriptor is invalid.");
                }

                break;
            case SstV5ProtocolConstants.StreamMarker:
                if (descriptor.SourceDescriptorCount != 0 ||
                    descriptor.TimingModelId != SstV5ProtocolConstants.TimingMonotonicEventStatus ||
                    descriptor.CompactPayloadRecordBytes != 1 ||
                    descriptor.AcceptedSensorMask != 0 ||
                    descriptor.AcceptedExtensionMask != 0)
                {
                    throw new FormatException("SST v5 marker descriptor is invalid.");
                }

                break;
        }
    }

    private static void ValidateFixedSourceStream(StreamDescriptorBuilder descriptor, byte expectedTimingModel)
    {
        if (descriptor.TimingModelId != expectedTimingModel ||
            descriptor.AcceptedExtensionMask != 0 ||
            descriptor.AcceptedRateMhz == 0 ||
            descriptor.SourceDescriptorCount == 0)
        {
            throw new FormatException("SST v5 fixed-rate stream descriptor is invalid.");
        }
    }

    private static void ValidateGpsStreamDescriptor(StreamDescriptorBuilder descriptor)
    {
        if (descriptor.SourceDescriptorCount != 0 ||
            descriptor.TimingModelId != SstV5ProtocolConstants.TimingGpsReceiverTimed ||
            descriptor.AcceptedSensorMask != SstV5ProtocolConstants.SensorGps ||
            descriptor.GpsPayloadEncodingId != SstV5ProtocolConstants.EncodingGpsNavFixV1 ||
            descriptor.GpsDriverId is not SstV5ProtocolConstants.GpsDriverLc76G
                and not SstV5ProtocolConstants.GpsDriverM8N ||
            (descriptor.AcceptedExtensionMask & ~SstV5ProtocolConstants.ExtensionGpsDiagPublicV1) != 0)
        {
            throw new FormatException("SST v5 GPS descriptor is invalid.");
        }

        var expectedPayloadBytes = descriptor.GpsDriverId == SstV5ProtocolConstants.GpsDriverLc76G
            ? SstV5ProtocolConstants.Lc76GRecordSize
            : SstV5ProtocolConstants.M8NRecordSize;
        var expectedExtensionBytes = descriptor.AcceptedExtensionMask == 0
            ? 0
            : SstV5ProtocolConstants.GpsDiagnosticsRecordSize;
        if (descriptor.GpsPayloadRecordBytes != expectedPayloadBytes ||
            descriptor.GpsExtensionRecordBytes != expectedExtensionBytes ||
            descriptor.CompactPayloadRecordBytes != expectedPayloadBytes + expectedExtensionBytes)
        {
            throw new FormatException("SST v5 GPS descriptor payload size is invalid.");
        }
    }

    private sealed class StreamDescriptorBuilder
    {
        public byte StreamKind { get; init; }
        public byte TimingModelId { get; init; }
        public byte SourceDescriptorCount { get; init; }
        public uint AcceptedSensorMask { get; set; }
        public uint AcceptedExtensionMask { get; set; }
        public uint AcceptedRateMhz { get; set; }
        public uint AcceptedBatchDurationMs { get; set; }
        public ushort CompactPayloadRecordBytes { get; set; }
        public byte GpsDriverId { get; set; }
        public byte GpsPayloadEncodingId { get; set; }
        public ushort GpsPayloadRecordBytes { get; set; }
        public ushort GpsExtensionRecordBytes { get; set; }
        public List<SourceDescriptorBuilder> Sources { get; } = [];

        public SstV5StreamDescriptor ToDescriptor() => new()
        {
            StreamKind = StreamKind,
            TimingModelId = TimingModelId,
            SourceDescriptorCount = SourceDescriptorCount,
            AcceptedSensorMask = AcceptedSensorMask,
            AcceptedExtensionMask = AcceptedExtensionMask,
            AcceptedRateMhz = AcceptedRateMhz,
            AcceptedBatchDurationMs = AcceptedBatchDurationMs,
            CompactPayloadRecordBytes = CompactPayloadRecordBytes,
            GpsDriverId = GpsDriverId,
            GpsPayloadEncodingId = GpsPayloadEncodingId,
            GpsPayloadRecordBytes = GpsPayloadRecordBytes,
            GpsExtensionRecordBytes = GpsExtensionRecordBytes,
            Sources = Sources.Select(source => source.ToDescriptor()).ToArray(),
        };
    }

    private sealed class SourceDescriptorBuilder
    {
        public byte StreamKind { get; init; }
        public byte DriverId { get; init; }
        public byte PayloadEncodingId { get; init; }
        public byte PayloadValueCount { get; init; }
        public uint SourceBitMask { get; init; }
        public ushort PayloadByteOffset { get; init; }
        public ushort PayloadRecordBytes { get; init; }
        public ushort PayloadValueWidthBits { get; init; }
        public ushort CountMin { get; set; }
        public ushort CountMax { get; set; }
        public ushort WrapModulus { get; set; }
        public byte Wraps { get; set; }
        public float AccelLsbPerG { get; set; }
        public float GyroLsbPerDps { get; set; }
        public byte BikeFrameId { get; set; }
        public float TemperatureLsbPerCelsius { get; set; }
        public float TemperatureCelsiusAtRawZero { get; set; }

        public SstV5SourceDescriptor ToDescriptor() => new()
        {
            StreamKind = StreamKind,
            DriverId = DriverId,
            PayloadEncodingId = PayloadEncodingId,
            PayloadValueCount = PayloadValueCount,
            SourceBitMask = SourceBitMask,
            PayloadByteOffset = PayloadByteOffset,
            PayloadRecordBytes = PayloadRecordBytes,
            PayloadValueWidthBits = PayloadValueWidthBits,
            CountMin = CountMin,
            CountMax = CountMax,
            WrapModulus = WrapModulus,
            Wraps = Wraps,
            AccelLsbPerG = AccelLsbPerG,
            GyroLsbPerDps = GyroLsbPerDps,
            BikeFrameId = BikeFrameId,
            TemperatureLsbPerCelsius = TemperatureLsbPerCelsius,
            TemperatureCelsiusAtRawZero = TemperatureCelsiusAtRawZero,
        };
    }
}

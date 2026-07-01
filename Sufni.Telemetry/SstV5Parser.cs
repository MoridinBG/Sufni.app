using System.Buffers.Binary;

namespace Sufni.Telemetry;

public class SstV5Parser : ISstParser
{
    private const int HeaderRemainderSize = 20;
    private const int FixedHeaderBytes = 24;
    private const int ChunkEnvelopeSize = 8;
    private const int MetadataHeaderSize = 12;
    private const int StreamDescriptorSize = 24;
    private const int GpsStreamDescriptorTailSize = 8;
    private const int SourceDescriptorSize = 16;
    private const int TravelSourceDescriptorTailSize = 8;
    private const int ImuSourceDescriptorTailSize = 12;
    private const int TemperatureSourceDescriptorTailSize = 8;
    private const int OmissionRecordSize = 8;
    private const int DataHeaderSize = 26;
    private const int FinalStatusHeaderSize = 12;
    private const int FinalStatusRecordSize = 36;
    private const int Lc76GRecordSize = 38;
    private const int M8NRecordSize = 30;
    private const int GpsDiagnosticsRecordSize = 17;
    private const string UnsupportedTravelMessage = "SST v5 travel data is missing or unsupported by this app.";
    private const string MissingFinalStatusMessage = "SST v5 final status is missing; parsed complete data chunks only.";
    private const string TrimmedTrailingChunkMessage = "SST v5 chunk extends past end of file; incomplete trailing chunk data was trimmed.";

    public SstFileInspection Inspect(BinaryReader reader, byte version)
    {
        var bytes = SstParserBytes.ReadRemainingBytes(reader);
        try
        {
            var raw = ParseBytes(bytes, version);
            return new ValidSstFileInspection(
                Version: version,
                StartTime: DateTimeOffset.FromUnixTimeMilliseconds(raw.SessionStartUtcMs).LocalDateTime,
                Duration: TimeSpan.FromSeconds(raw.RecordingDurationSeconds ?? 0),
                TelemetrySampleRate: raw.SampleRate,
                HasUnknown: false,
                MalformedMessage: raw.MalformedMessage);
        }
        catch (SstV5MalformedException ex)
        {
            return new MalformedSstFileInspection(
                Version: version,
                StartTime: ex.StartTime,
                Duration: ex.Duration,
                TelemetrySampleRate: ex.TelemetrySampleRate,
                Message: ex.Message);
        }
    }

    public RawTelemetryData Parse(BinaryReader reader, byte version)
    {
        var bytes = SstParserBytes.ReadRemainingBytes(reader);
        try
        {
            return ParseBytes(bytes, version);
        }
        catch (SstV5MalformedException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
    }

    private static RawTelemetryData ParseBytes(byte[] bytes, byte version)
    {
        var context = new V5ParseContext(version);
        if (bytes.Length < HeaderRemainderSize)
        {
            throw context.Malformed("SST v5 header is truncated.");
        }

        var cursor = new SstByteReader(bytes);
        var headerBytes = cursor.ReadUInt16();
        var fileFlags = cursor.ReadUInt16();
        var sessionStartUtcMs = cursor.ReadInt64();
        _ = cursor.ReadUInt64(); // session_start_monotonic_us is the anchor; chunk deltas are already relative to it.

        context.SessionStartUtcMs = sessionStartUtcMs;

        if (headerBytes != FixedHeaderBytes)
        {
            throw context.Malformed("SST v5 header length is invalid.");
        }

        if (fileFlags != 0)
        {
            throw context.Malformed("SST v5 file flags are invalid.");
        }

        while (cursor.Position < cursor.Length)
        {
            if (cursor.Position + ChunkEnvelopeSize > cursor.Length)
            {
                throw context.Malformed("SST v5 file ends with an incomplete chunk header.");
            }

            var chunkStart = cursor.Position;
            var chunkType = cursor.ReadUInt16();
            var chunkFlags = cursor.ReadUInt16();
            var declaredLength = cursor.ReadUInt32();
            var payloadStart = cursor.Position;
            var declaredEnd = (long)payloadStart + declaredLength;

            if (declaredEnd > cursor.Length)
            {
                if (context.MetadataParsed && context.HasCompleteTravelData)
                {
                    context.WarningMessage ??= TrimmedTrailingChunkMessage;
                    break;
                }

                throw context.Malformed("SST v5 chunk extends past end of file.");
            }

            if (chunkFlags != 0)
            {
                throw context.Malformed("SST v5 chunk flags are invalid.");
            }

            var payloadLength = checked((int)declaredLength);
            var payload = bytes.AsSpan(payloadStart, payloadLength);
            cursor.MoveTo((int)declaredEnd);

            if (!IsKnownChunkType(chunkType))
            {
                throw context.Malformed("SST v5 chunk type is unknown.");
            }

            if (context.FinalStatus is not null)
            {
                throw context.Malformed("SST v5 final status must be the last chunk.");
            }

            if (chunkType == SstV5Constants.ChunkSessionMetadata)
            {
                if (context.MetadataParsed || chunkStart != HeaderRemainderSize)
                {
                    throw context.Malformed("SST v5 session metadata chunk is out of order.");
                }

                ParseMetadata(payload, context);
                continue;
            }

            if (!context.MetadataParsed)
            {
                throw context.Malformed("SST v5 data chunk appears before session metadata.");
            }

            if (chunkType == SstV5Constants.ChunkFinalStatus)
            {
                ParseFinalStatus(payload, context);
                continue;
            }

            ParseDataChunk(chunkType, payload, context);
        }

        if (!context.MetadataParsed)
        {
            throw context.Malformed("SST v5 session metadata is missing.");
        }

        return context.BuildRaw();
    }

    private static void ParseMetadata(ReadOnlySpan<byte> payload, V5ParseContext context)
    {
        try
        {
            var reader = new SstByteReader(payload);
            if (payload.Length < MetadataHeaderSize)
            {
                throw context.Malformed("SST v5 metadata payload is truncated.");
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
                throw context.Malformed("SST v5 board ID is invalid.");
            }

            if (metadataFlags != 0 || reserved != 0)
            {
                throw context.Malformed("SST v5 metadata flags are invalid.");
            }

            byte previousStreamKind = 0;
            uint emittedStreamMask = 0;
            for (var index = 0; index < streamDescriptorCount; index++)
            {
                var descriptor = ReadStreamDescriptor(ref reader, context);
                if (descriptor.StreamKind <= previousStreamKind)
                {
                    throw context.Malformed("SST v5 stream descriptors are out of order.");
                }

                previousStreamKind = descriptor.StreamKind;
                emittedStreamMask |= StreamMaskForKind(descriptor.StreamKind);
                context.StreamDescriptors.Add(descriptor.StreamKind, descriptor);
                context.StreamDescriptorOrder.Add(descriptor);
            }

            if (emittedStreamMask != acceptedStreamMask)
            {
                throw context.Malformed("SST v5 accepted stream mask does not match descriptors.");
            }

            byte previousSourceStreamKind = 0;
            uint previousSourceMask = 0;
            for (var index = 0; index < sourceDescriptorTotalCount; index++)
            {
                var source = ReadSourceDescriptor(ref reader, context);
                if (source.StreamKind < previousSourceStreamKind ||
                    (source.StreamKind == previousSourceStreamKind && source.SourceBitMask <= previousSourceMask))
                {
                    throw context.Malformed("SST v5 source descriptors are out of order.");
                }

                previousSourceStreamKind = source.StreamKind;
                previousSourceMask = source.SourceBitMask;
                context.StreamDescriptors[source.StreamKind].Sources.Add(source);
            }

            foreach (var descriptor in context.StreamDescriptorOrder)
            {
                if (descriptor.Sources.Count != descriptor.SourceDescriptorCount)
                {
                    throw context.Malformed("SST v5 source descriptor count does not match stream descriptor.");
                }

                ValidateStreamDescriptorShape(descriptor, context);
            }

            for (var index = 0; index < omissionCount; index++)
            {
                if (reader.Remaining < OmissionRecordSize)
                {
                    throw context.Malformed("SST v5 omission records are truncated.");
                }

                var targetKind = reader.ReadByte();
                _ = reader.ReadByte(); // stream_kind
                _ = reader.ReadByte(); // admission_reason
                var omissionReserved = reader.ReadByte();
                _ = reader.ReadUInt32(); // target_mask
                if (targetKind is < 1 or > 3 || omissionReserved != 0)
                {
                    throw context.Malformed("SST v5 omission record is invalid.");
                }
            }

            if (reader.Position != reader.Length)
            {
                throw context.Malformed("SST v5 metadata payload length is invalid.");
            }

            context.MetadataParsed = true;
            context.InitializeImuMetadata();
        }
        catch (FormatException ex)
        {
            throw context.Malformed("SST v5 metadata payload cannot be fully parsed.", ex);
        }
    }

    private static V5StreamDescriptor ReadStreamDescriptor(ref SstByteReader reader, V5ParseContext context)
    {
        if (reader.Remaining < StreamDescriptorSize)
        {
            throw context.Malformed("SST v5 stream descriptor is truncated.");
        }

        var streamKind = reader.ReadByte();
        var timingModelId = reader.ReadByte();
        var sourceDescriptorCount = reader.ReadByte();
        var reserved = reader.ReadByte();
        var acceptedSensorMask = reader.ReadUInt32();
        var acceptedExtensionMask = reader.ReadUInt32();
        var acceptedRateMhz = reader.ReadUInt32();
        var acceptedBatchDurationMs = reader.ReadUInt32();
        var compactPayloadRecordBytes = reader.ReadUInt16();
        var flags = reader.ReadUInt16();

        if (!IsKnownStreamKind(streamKind) || timingModelId is < 1 or > 3 || reserved != 0 || flags != 0)
        {
            throw context.Malformed("SST v5 stream descriptor is invalid.");
        }

        var descriptor = new V5StreamDescriptor(
            streamKind,
            timingModelId,
            sourceDescriptorCount,
            acceptedSensorMask,
            acceptedExtensionMask,
            acceptedRateMhz,
            acceptedBatchDurationMs,
            compactPayloadRecordBytes);

        if (streamKind == SstV5Constants.StreamGps)
        {
            if (reader.Remaining < GpsStreamDescriptorTailSize)
            {
                throw context.Malformed("SST v5 GPS stream descriptor is truncated.");
            }

            descriptor.GpsDriverId = reader.ReadByte();
            descriptor.GpsPayloadEncodingId = reader.ReadByte();
            descriptor.GpsPayloadRecordBytes = reader.ReadUInt16();
            descriptor.GpsExtensionRecordBytes = reader.ReadUInt16();
            var gpsReserved = reader.ReadUInt16();
            if (gpsReserved != 0)
            {
                throw context.Malformed("SST v5 GPS stream descriptor reserved field is invalid.");
            }
        }

        return descriptor;
    }

    private static V5SourceDescriptor ReadSourceDescriptor(ref SstByteReader reader, V5ParseContext context)
    {
        if (reader.Remaining < SourceDescriptorSize)
        {
            throw context.Malformed("SST v5 source descriptor is truncated.");
        }

        var source = new V5SourceDescriptor
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

        if (flags != 0 || !context.StreamDescriptors.TryGetValue(source.StreamKind, out var streamDescriptor))
        {
            throw context.Malformed("SST v5 source descriptor is invalid.");
        }

        if (source.StreamKind is not SstV5Constants.StreamTravel and not SstV5Constants.StreamImu and not SstV5Constants.StreamTemperature)
        {
            throw context.Malformed("SST v5 source descriptor references a non-source stream.");
        }

        if (!IsSingleBitMask(source.SourceBitMask) || (source.SourceBitMask & streamDescriptor.AcceptedSensorMask) == 0)
        {
            throw context.Malformed("SST v5 source descriptor source bit is invalid.");
        }

        if (!SourceBelongsToStream(source.StreamKind, source.SourceBitMask))
        {
            throw context.Malformed("SST v5 source descriptor source bit does not belong to the stream.");
        }

        if (source.PayloadByteOffset + source.PayloadRecordBytes > streamDescriptor.CompactPayloadRecordBytes)
        {
            throw context.Malformed("SST v5 source descriptor payload range is invalid.");
        }

        switch (source.StreamKind)
        {
            case SstV5Constants.StreamTravel:
                ReadTravelSourceTail(ref reader, source, context);
                break;
            case SstV5Constants.StreamImu:
                ReadImuSourceTail(ref reader, source, context);
                break;
            case SstV5Constants.StreamTemperature:
                ReadTemperatureSourceTail(ref reader, source, context);
                break;
        }

        return source;
    }

    private static void ReadTravelSourceTail(ref SstByteReader reader, V5SourceDescriptor source, V5ParseContext context)
    {
        if (reader.Remaining < TravelSourceDescriptorTailSize)
        {
            throw context.Malformed("SST v5 travel source descriptor is truncated.");
        }

        source.CountMin = reader.ReadUInt16();
        source.CountMax = reader.ReadUInt16();
        source.WrapModulus = reader.ReadUInt16();
        source.Wraps = reader.ReadByte();
        var reserved = reader.ReadByte();

        if (reserved != 0 ||
            source.PayloadEncodingId != SstV5Constants.EncodingTravelAdjustedU16 ||
            source.PayloadRecordBytes != 2 ||
            source.PayloadValueWidthBits != 16 ||
            source.PayloadValueCount != 1 ||
            source.Wraps is not 0 and not 1)
        {
            throw context.Malformed("SST v5 travel source descriptor is invalid.");
        }
    }

    private static void ReadImuSourceTail(ref SstByteReader reader, V5SourceDescriptor source, V5ParseContext context)
    {
        if (reader.Remaining < ImuSourceDescriptorTailSize)
        {
            throw context.Malformed("SST v5 IMU source descriptor is truncated.");
        }

        source.AccelLsbPerG = reader.ReadSingle();
        source.GyroLsbPerDps = reader.ReadSingle();
        source.BikeFrameId = reader.ReadByte();
        var reserved = reader.ReadBytes(3);

        if (reserved[0] != 0 ||
            reserved[1] != 0 ||
            reserved[2] != 0 ||
            source.PayloadEncodingId != SstV5Constants.EncodingImuI16X6CalCounts ||
            source.PayloadRecordBytes != 12 ||
            source.PayloadValueCount != 6 ||
            source.BikeFrameId != 1)
        {
            throw context.Malformed("SST v5 IMU source descriptor is invalid.");
        }
    }

    private static void ReadTemperatureSourceTail(ref SstByteReader reader, V5SourceDescriptor source, V5ParseContext context)
    {
        if (reader.Remaining < TemperatureSourceDescriptorTailSize)
        {
            throw context.Malformed("SST v5 temperature source descriptor is truncated.");
        }

        source.TemperatureLsbPerCelsius = reader.ReadSingle();
        source.TemperatureCelsiusAtRawZero = reader.ReadSingle();

        if (source.PayloadEncodingId != SstV5Constants.EncodingTemperatureRawI16 ||
            source.PayloadRecordBytes != 2 ||
            source.PayloadValueCount != 1 ||
            source.PayloadValueWidthBits != 16 ||
            source.TemperatureLsbPerCelsius == 0)
        {
            throw context.Malformed("SST v5 temperature source descriptor is invalid.");
        }
    }

    private static void ValidateStreamDescriptorShape(V5StreamDescriptor descriptor, V5ParseContext context)
    {
        switch (descriptor.StreamKind)
        {
            case SstV5Constants.StreamTravel:
                ValidateFixedSourceStream(descriptor, SstV5Constants.TimingFixedRate, context);
                if ((descriptor.AcceptedSensorMask & ~(SstV5Constants.SensorForkTravel | SstV5Constants.SensorShockTravel)) != 0)
                {
                    throw context.Malformed("SST v5 travel descriptor accepted sensor mask is invalid.");
                }
                break;
            case SstV5Constants.StreamImu:
                ValidateFixedSourceStream(descriptor, SstV5Constants.TimingFixedRate, context);
                if ((descriptor.AcceptedSensorMask & ~(SstV5Constants.SensorFrameImu | SstV5Constants.SensorForkImu | SstV5Constants.SensorRearImu)) != 0)
                {
                    throw context.Malformed("SST v5 IMU descriptor accepted sensor mask is invalid.");
                }
                break;
            case SstV5Constants.StreamTemperature:
                if (descriptor.TimingModelId != SstV5Constants.TimingMonotonicEventStatus ||
                    (descriptor.AcceptedSensorMask & ~(SstV5Constants.SensorFrameImu | SstV5Constants.SensorForkImu | SstV5Constants.SensorRearImu)) != 0)
                {
                    throw context.Malformed("SST v5 temperature descriptor is invalid.");
                }
                break;
            case SstV5Constants.StreamGps:
                ValidateGpsStreamDescriptor(descriptor, context);
                break;
            case SstV5Constants.StreamBattery:
                if (descriptor.SourceDescriptorCount != 0 ||
                    descriptor.TimingModelId != SstV5Constants.TimingMonotonicEventStatus ||
                    descriptor.CompactPayloadRecordBytes != 4 ||
                    descriptor.AcceptedSensorMask != SstV5Constants.SensorBattery ||
                    descriptor.AcceptedExtensionMask != 0)
                {
                    throw context.Malformed("SST v5 battery descriptor is invalid.");
                }
                break;
            case SstV5Constants.StreamMarker:
                if (descriptor.SourceDescriptorCount != 0 ||
                    descriptor.TimingModelId != SstV5Constants.TimingMonotonicEventStatus ||
                    descriptor.CompactPayloadRecordBytes != 1 ||
                    descriptor.AcceptedSensorMask != 0 ||
                    descriptor.AcceptedExtensionMask != 0)
                {
                    throw context.Malformed("SST v5 marker descriptor is invalid.");
                }
                break;
        }
    }

    private static void ValidateFixedSourceStream(V5StreamDescriptor descriptor, byte expectedTimingModel, V5ParseContext context)
    {
        if (descriptor.TimingModelId != expectedTimingModel ||
            descriptor.AcceptedExtensionMask != 0 ||
            descriptor.AcceptedRateMhz == 0 ||
            descriptor.SourceDescriptorCount == 0)
        {
            throw context.Malformed("SST v5 fixed-rate stream descriptor is invalid.");
        }
    }

    private static void ValidateGpsStreamDescriptor(V5StreamDescriptor descriptor, V5ParseContext context)
    {
        if (descriptor.SourceDescriptorCount != 0 ||
            descriptor.TimingModelId != SstV5Constants.TimingGpsReceiverTimed ||
            descriptor.AcceptedSensorMask != SstV5Constants.SensorGps ||
            descriptor.GpsPayloadEncodingId != SstV5Constants.EncodingGpsNavFixV1 ||
            descriptor.GpsDriverId is not SstV5Constants.GpsDriverLc76G and not SstV5Constants.GpsDriverM8N ||
            (descriptor.AcceptedExtensionMask & ~SstV5Constants.ExtensionGpsDiagPublicV1) != 0)
        {
            throw context.Malformed("SST v5 GPS descriptor is invalid.");
        }

        var expectedPayloadBytes = descriptor.GpsDriverId == SstV5Constants.GpsDriverLc76G
            ? Lc76GRecordSize
            : M8NRecordSize;
        var expectedExtensionBytes = descriptor.AcceptedExtensionMask == 0 ? 0 : GpsDiagnosticsRecordSize;
        if (descriptor.GpsPayloadRecordBytes != expectedPayloadBytes ||
            descriptor.GpsExtensionRecordBytes != expectedExtensionBytes ||
            descriptor.CompactPayloadRecordBytes != expectedPayloadBytes + expectedExtensionBytes)
        {
            throw context.Malformed("SST v5 GPS descriptor payload size is invalid.");
        }
    }

    private static void ParseDataChunk(ushort chunkType, ReadOnlySpan<byte> payload, V5ParseContext context)
    {
        var streamKind = StreamKindForDataChunk(chunkType);
        if (!context.StreamDescriptors.TryGetValue(streamKind, out var descriptor))
        {
            throw context.Malformed("SST v5 data chunk references a stream without a descriptor.");
        }

        if (payload.Length < DataHeaderSize)
        {
            throw context.Malformed("SST v5 data payload is truncated.");
        }

        var reader = new SstByteReader(payload);
        var firstIndex = reader.ReadUInt64();
        var firstMonotonicDeltaUs = reader.ReadUInt64();
        var sampleCount = reader.ReadUInt32();
        var validityMask = reader.ReadUInt32();
        var flags = reader.ReadUInt16();

        if (sampleCount == 0 || flags != 0)
        {
            throw context.Malformed("SST v5 data header is invalid.");
        }

        var expectedLength = DataHeaderSize + (ulong)sampleCount * descriptor.CompactPayloadRecordBytes;
        if (expectedLength != (ulong)payload.Length)
        {
            throw context.Malformed("SST v5 data payload length is invalid.");
        }

        if (streamKind == SstV5Constants.StreamMarker)
        {
            if (validityMask != 0)
            {
                throw context.Malformed("SST v5 marker validity mask is invalid.");
            }
        }
        else if ((validityMask & ~descriptor.AcceptedSensorMask) != 0)
        {
            throw context.Malformed("SST v5 validity mask contains unaccepted source bits.");
        }

        var compactPayload = payload[DataHeaderSize..];
        switch (streamKind)
        {
            case SstV5Constants.StreamTravel:
                DecodeTravelData(descriptor, compactPayload, firstIndex, firstMonotonicDeltaUs, sampleCount, validityMask, context);
                break;
            case SstV5Constants.StreamImu:
                DecodeImuData(descriptor, compactPayload, firstIndex, firstMonotonicDeltaUs, sampleCount, validityMask, context);
                break;
            case SstV5Constants.StreamTemperature:
                DecodeTemperatureData(descriptor, compactPayload, firstIndex, firstMonotonicDeltaUs, sampleCount, validityMask, context);
                break;
            case SstV5Constants.StreamGps:
                DecodeGpsData(descriptor, compactPayload, firstIndex, firstMonotonicDeltaUs, sampleCount, validityMask, context);
                break;
            case SstV5Constants.StreamBattery:
                DecodeBatteryData(descriptor, compactPayload, firstIndex, firstMonotonicDeltaUs, sampleCount, validityMask, context);
                break;
            case SstV5Constants.StreamMarker:
                DecodeMarkerData(descriptor, compactPayload, firstIndex, firstMonotonicDeltaUs, sampleCount, context);
                break;
        }

        context.HasCompleteTravelData |= streamKind == SstV5Constants.StreamTravel;
        context.ObserveDataEnd(descriptor, firstMonotonicDeltaUs, sampleCount);
    }

    private static void DecodeTravelData(
        V5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        uint sampleCount,
        uint validityMask,
        V5ParseContext context)
    {
        foreach (var source in descriptor.Sources)
        {
            var builder = source.SourceBitMask switch
            {
                SstV5Constants.SensorForkTravel => context.FrontBuilder,
                SstV5Constants.SensorShockTravel => context.RearBuilder,
                _ => throw context.Malformed("SST v5 travel source bit is unsupported by this app."),
            };

            if ((validityMask & source.SourceBitMask) == 0)
            {
                builder.AddInvalidRange(firstIndex, sampleCount, firstMonotonicDeltaUs, descriptor.AcceptedRateMhz);
                continue;
            }

            for (var sampleOffset = 0u; sampleOffset < sampleCount; sampleOffset++)
            {
                var recordOffset = checked((int)(sampleOffset * descriptor.CompactPayloadRecordBytes + source.PayloadByteOffset));
                var value = BinaryPrimitives.ReadUInt16LittleEndian(compactPayload.Slice(recordOffset, sizeof(ushort)));
                var index = firstIndex + sampleOffset;
                var monotonic = CalculateSampleMonotonicUs(firstMonotonicDeltaUs, sampleOffset, descriptor.AcceptedRateMhz);
                builder.AddValidSample(index, monotonic, value, descriptor.AcceptedRateMhz);
            }
        }
    }

    private static void DecodeImuData(
        V5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        uint sampleCount,
        uint validityMask,
        V5ParseContext context)
    {
        foreach (var source in descriptor.Sources)
        {
            var location = LocationIdForImuSource(source.SourceBitMask, context);
            var builder = context.ImuBuilders[location];

            if ((validityMask & source.SourceBitMask) == 0)
            {
                builder.AddInvalidRange(firstIndex, sampleCount, firstMonotonicDeltaUs, descriptor.AcceptedRateMhz);
                continue;
            }

            for (var sampleOffset = 0u; sampleOffset < sampleCount; sampleOffset++)
            {
                var recordOffset = checked((int)(sampleOffset * descriptor.CompactPayloadRecordBytes + source.PayloadByteOffset));
                var record = ReadImuRecord(compactPayload.Slice(recordOffset, 12));
                var index = firstIndex + sampleOffset;
                var monotonic = CalculateSampleMonotonicUs(firstMonotonicDeltaUs, sampleOffset, descriptor.AcceptedRateMhz);
                builder.AddValidSample(index, monotonic, record, descriptor.AcceptedRateMhz);
            }
        }
    }

    private static void DecodeTemperatureData(
        V5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        uint sampleCount,
        uint validityMask,
        V5ParseContext context)
    {
        foreach (var source in descriptor.Sources)
        {
            if ((validityMask & source.SourceBitMask) == 0)
            {
                context.AddGap(
                    SstV5Constants.StreamTemperature,
                    LocationIdForImuSource(source.SourceBitMask, context),
                    firstIndex,
                    sampleCount,
                    null,
                    "invalid_validity");
                continue;
            }

            for (var sampleOffset = 0u; sampleOffset < sampleCount; sampleOffset++)
            {
                var recordOffset = checked((int)(sampleOffset * descriptor.CompactPayloadRecordBytes + source.PayloadByteOffset));
                var raw = BinaryPrimitives.ReadInt16LittleEndian(compactPayload.Slice(recordOffset, sizeof(short)));
                var celsius = raw / source.TemperatureLsbPerCelsius + source.TemperatureCelsiusAtRawZero;
                var monotonic = descriptor.AcceptedRateMhz > 0
                    ? CalculateSampleMonotonicUs(firstMonotonicDeltaUs, sampleOffset, descriptor.AcceptedRateMhz)
                    : firstMonotonicDeltaUs;
                var timestampUtc = checked((context.SessionStartUtcMs!.Value * 1000 + (long)monotonic) / 1_000_000);
                context.TemperatureSamples.Add(new TemperatureSample(
                    timestampUtc,
                    LocationIdForImuSource(source.SourceBitMask, context),
                    celsius));
            }
        }
    }

    private static void DecodeGpsData(
        V5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        uint sampleCount,
        uint validityMask,
        V5ParseContext context)
    {
        if ((validityMask & SstV5Constants.SensorGps) == 0)
        {
            context.AddGap(SstV5Constants.StreamGps, null, firstIndex, sampleCount, null, "invalid_validity");
            return;
        }

        for (var sampleOffset = 0u; sampleOffset < sampleCount; sampleOffset++)
        {
            var recordOffset = checked((int)(sampleOffset * descriptor.CompactPayloadRecordBytes));
            var record = DecodeGpsRecord(descriptor, compactPayload.Slice(recordOffset, descriptor.CompactPayloadRecordBytes), context);
            if (record is not null)
            {
                context.GpsRecords.Add(record);
            }
        }
    }

    private static GpsRecord? DecodeGpsRecord(
        V5StreamDescriptor descriptor,
        ReadOnlySpan<byte> recordBytes,
        V5ParseContext context)
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

        if (descriptor.GpsDriverId == SstV5Constants.GpsDriverLc76G)
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
            // Out-of-range fix modes are unpublishable GPS records; skip them like invalid
            // dates rather than failing the whole import.
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

    private static void DecodeBatteryData(
        V5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        uint sampleCount,
        uint validityMask,
        V5ParseContext context)
    {
        if ((validityMask & SstV5Constants.SensorBattery) == 0)
        {
            context.AddGap(SstV5Constants.StreamBattery, null, firstIndex, sampleCount, null, "invalid_validity");
            return;
        }

        // Battery records are size-validated by the chunk-length check and intentionally not
        // stored in app data. Undefined battery flag bits are tolerated rather than failing the
        // import, since a future appendix may define additional bits.
    }

    private static void DecodeMarkerData(
        V5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        ulong firstIndex,
        ulong firstMonotonicDeltaUs,
        uint sampleCount,
        V5ParseContext context)
    {
        for (var sampleOffset = 0u; sampleOffset < sampleCount; sampleOffset++)
        {
            var recordOffset = checked((int)(sampleOffset * descriptor.CompactPayloadRecordBytes));
            var markerType = compactPayload[recordOffset];
            if (markerType == SstV5Constants.MarkerManualUserMark)
            {
                context.Markers.Add(new MarkerData(firstMonotonicDeltaUs / 1_000_000.0));
            }
        }
    }

    private static void ParseFinalStatus(ReadOnlySpan<byte> payload, V5ParseContext context)
    {
        if (payload.Length < FinalStatusHeaderSize)
        {
            throw context.Malformed("SST v5 final status payload is truncated.");
        }

        var reader = new SstByteReader(payload);
        var sessionResultReason = reader.ReadByte();
        var streamStatusCount = reader.ReadByte();
        var finalFlags = reader.ReadUInt16();
        var stoppedMonotonicDeltaUs = reader.ReadUInt64();

        if (sessionResultReason is not 1 and not 2 and not 4 and not 5 and not 6 || finalFlags != 0)
        {
            throw context.Malformed("SST v5 final status header is invalid.");
        }

        if (streamStatusCount != context.StreamDescriptorOrder.Count ||
            payload.Length != FinalStatusHeaderSize + streamStatusCount * FinalStatusRecordSize)
        {
            throw context.Malformed("SST v5 final status payload length is invalid.");
        }

        var statuses = new SstStreamFinalStatus[streamStatusCount];
        for (var index = 0; index < streamStatusCount; index++)
        {
            var descriptor = context.StreamDescriptorOrder[index];
            var producerState = reader.ReadByte();
            var producerFailureReason = reader.ReadByte();
            var reserved = reader.ReadUInt16();
            var producerMissedCount = reader.ReadUInt64();
            var producerMissingTimeUs = reader.ReadUInt64();
            var sinkMissedCount = reader.ReadUInt64();
            var sinkMissingTimeUs = reader.ReadUInt64();

            // Reserved bytes must be zero, but out-of-range producer state / failure reason
            // values are tolerated and stored raw: the spec treats an invalid producer state as
            // a producer fault rather than a malformed file, and this metadata does not gate import.
            if (reserved != 0)
            {
                throw context.Malformed("SST v5 final status record is invalid.");
            }

            var status = new SstStreamFinalStatus
            {
                StreamKind = descriptor.StreamKind,
                ProducerState = producerState,
                ProducerFailureReason = producerFailureReason,
                ProducerMissedCount = producerMissedCount,
                ProducerMissingTimeUs = producerMissingTimeUs,
                SinkMissedCount = sinkMissedCount,
                SinkMissingTimeUs = sinkMissingTimeUs,
            };

            statuses[index] = status;
            if (producerMissedCount > 0)
            {
                context.AddGap(descriptor.StreamKind, null, 0, producerMissedCount, OptionalMissingTime(producerMissingTimeUs), "final_status_producer");
            }

            if (sinkMissedCount > 0)
            {
                context.AddGap(descriptor.StreamKind, null, 0, sinkMissedCount, OptionalMissingTime(sinkMissingTimeUs), "final_status_sink");
            }
        }

        context.FinalStatus = new SstFinalStatus
        {
            SessionResultReason = sessionResultReason,
            StoppedMonotonicDeltaUs = stoppedMonotonicDeltaUs,
            Streams = statuses,
        };
        context.MaxObservedEndUs = Math.Max(context.MaxObservedEndUs, stoppedMonotonicDeltaUs);
    }

    private static RawImuData? BuildImuData(V5ParseContext context)
    {
        if (context.ImuData.Meta.Count == 0 && context.ImuBuilders.Count == 0)
        {
            return null;
        }

        foreach (var builder in context.ImuBuilders.Values)
        {
            builder.Finish();
            context.ImuData.Segments.AddRange(builder.Segments.Select(segment => new RawImuSegment
            {
                LocationId = builder.LocationId!.Value,
                FirstIndex = segment.FirstIndex,
                FirstMonotonicDeltaUs = segment.FirstMonotonicDeltaUs,
                Records = segment.Values.ToArray(),
            }));
        }

        var imuGapExists = context.Gaps.Any(gap => gap.StreamKind == SstV5Constants.StreamImu);
        var segmentsByLocation = context.ImuData.Segments
            .GroupBy(segment => segment.LocationId)
            .ToDictionary(group => group.Key, group => group.OrderBy(segment => segment.FirstIndex).ToArray());
        var dense = context.ImuData.ActiveLocations.Count > 0 &&
            context.ImuData.ActiveLocations.All(location => segmentsByLocation.TryGetValue(location, out var segments) && segments.Length == 1) &&
            !imuGapExists;

        if (dense)
        {
            var firstLocation = context.ImuData.ActiveLocations[0];
            var firstSegment = segmentsByLocation[firstLocation][0];
            dense = context.ImuData.ActiveLocations.All(location =>
            {
                var segment = segmentsByLocation[location][0];
                return segment.FirstIndex == firstSegment.FirstIndex &&
                    segment.FirstMonotonicDeltaUs == firstSegment.FirstMonotonicDeltaUs &&
                    segment.Records.Length == firstSegment.Records.Length;
            });

            if (dense)
            {
                for (var sampleIndex = 0; sampleIndex < firstSegment.Records.Length; sampleIndex++)
                {
                    foreach (var location in context.ImuData.ActiveLocations)
                    {
                        context.ImuData.Records.Add(segmentsByLocation[location][0].Records[sampleIndex]);
                    }
                }
            }
        }

        context.ImuData.HasGaps = !dense ||
            context.ImuData.Segments.CountBy(segment => segment.LocationId).Any(kvp => kvp.Value > 1);

        return context.ImuData;
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

    private static bool IsKnownChunkType(ushort chunkType) =>
        chunkType is >= SstV5Constants.ChunkSessionMetadata and <= SstV5Constants.ChunkFinalStatus;

    private static bool IsKnownStreamKind(byte streamKind) =>
        streamKind is >= SstV5Constants.StreamTravel and <= SstV5Constants.StreamMarker;

    private static byte StreamKindForDataChunk(ushort chunkType) => chunkType switch
    {
        SstV5Constants.ChunkTravelData => SstV5Constants.StreamTravel,
        SstV5Constants.ChunkImuData => SstV5Constants.StreamImu,
        SstV5Constants.ChunkTemperatureData => SstV5Constants.StreamTemperature,
        SstV5Constants.ChunkGpsData => SstV5Constants.StreamGps,
        SstV5Constants.ChunkBatteryData => SstV5Constants.StreamBattery,
        SstV5Constants.ChunkMarkerData => SstV5Constants.StreamMarker,
        _ => throw new InvalidOperationException("Chunk type is not a data chunk."),
    };

    private static uint StreamMaskForKind(byte streamKind) => 1u << streamKind;

    private static bool IsSingleBitMask(uint mask) => mask != 0 && (mask & (mask - 1)) == 0;

    private static bool SourceBelongsToStream(byte streamKind, uint sourceBitMask) => streamKind switch
    {
        SstV5Constants.StreamTravel => (sourceBitMask & (SstV5Constants.SensorForkTravel | SstV5Constants.SensorShockTravel)) != 0,
        SstV5Constants.StreamImu => (sourceBitMask & (SstV5Constants.SensorFrameImu | SstV5Constants.SensorForkImu | SstV5Constants.SensorRearImu)) != 0,
        SstV5Constants.StreamTemperature => (sourceBitMask & (SstV5Constants.SensorFrameImu | SstV5Constants.SensorForkImu | SstV5Constants.SensorRearImu)) != 0,
        _ => false,
    };

    private static byte LocationIdForImuSource(uint sourceBitMask, V5ParseContext context) => sourceBitMask switch
    {
        SstV5Constants.SensorFrameImu => (byte)ImuLocation.Frame,
        SstV5Constants.SensorForkImu => (byte)ImuLocation.Fork,
        SstV5Constants.SensorRearImu => (byte)ImuLocation.Shock,
        _ => throw context.Malformed("SST v5 IMU location source bit is invalid."),
    };

    private static ulong CalculateSampleMonotonicUs(ulong firstMonotonicDeltaUs, ulong sampleOffset, uint rateMhz) =>
        checked(firstMonotonicDeltaUs + RoundDurationUs(sampleOffset, rateMhz));

    private static ulong RoundDurationUs(ulong sampleCount, uint rateMhz)
    {
        if (sampleCount == 0 || rateMhz == 0)
        {
            return 0;
        }

        return checked((ulong)Math.Round(sampleCount * 1_000_000_000.0 / rateMhz, MidpointRounding.AwayFromZero));
    }

    private static ulong? OptionalMissingTime(ulong missingTimeUs) => missingTimeUs == 0 ? null : missingTimeUs;

    private sealed class V5ParseContext
    {
        public byte Version { get; }
        public long? SessionStartUtcMs { get; set; }
        public bool MetadataParsed { get; set; }
        public bool HasCompleteTravelData { get; set; }
        public ulong MaxObservedEndUs { get; set; }
        public string? WarningMessage { get; set; }
        public SstFinalStatus? FinalStatus { get; set; }
        public Dictionary<byte, V5StreamDescriptor> StreamDescriptors { get; } = [];
        public List<V5StreamDescriptor> StreamDescriptorOrder { get; } = [];
        public List<RawStreamGap> Gaps { get; } = [];
        public List<MarkerData> Markers { get; } = [];
        public RawImuData ImuData { get; } = new();
        public List<GpsRecord> GpsRecords { get; } = [];
        public List<TemperatureSample> TemperatureSamples { get; } = [];
        public FixedSegmentBuilder<ushort> FrontBuilder { get; }
        public FixedSegmentBuilder<ushort> RearBuilder { get; }
        public Dictionary<byte, FixedSegmentBuilder<ImuRecord>> ImuBuilders { get; } = [];

        public V5ParseContext(byte version)
        {
            Version = version;
            // Tag travel gaps with the travel sensor bit (fork=front, shock=rear) so the
            // processing layer can attribute a gap to the correct side instead of both.
            FrontBuilder = new FixedSegmentBuilder<ushort>(SstV5Constants.StreamTravel, (byte)SstV5Constants.SensorForkTravel, AddGap);
            RearBuilder = new FixedSegmentBuilder<ushort>(SstV5Constants.StreamTravel, (byte)SstV5Constants.SensorShockTravel, AddGap);
        }

        public void InitializeImuMetadata()
        {
            if (!StreamDescriptors.TryGetValue(SstV5Constants.StreamImu, out var imuDescriptor))
            {
                return;
            }

            foreach (var source in imuDescriptor.Sources.OrderBy(source => source.SourceBitMask))
            {
                var location = LocationIdForImuSource(source.SourceBitMask, this);
                ImuData.Meta.Add(new ImuMetaEntry(location, source.AccelLsbPerG, source.GyroLsbPerDps));
                ImuData.ActiveLocations.Add(location);
                ImuBuilders[location] = new FixedSegmentBuilder<ImuRecord>(SstV5Constants.StreamImu, location, AddGap);
            }

            ImuData.SampleRate = imuDescriptor.AcceptedRateMhz % 1000 == 0
                ? checked((int)(imuDescriptor.AcceptedRateMhz / 1000))
                : 0;
        }

        public void ObserveDataEnd(V5StreamDescriptor descriptor, ulong firstMonotonicDeltaUs, uint sampleCount)
        {
            var endUs = descriptor.TimingModelId == SstV5Constants.TimingFixedRate
                ? CalculateSampleMonotonicUs(firstMonotonicDeltaUs, sampleCount, descriptor.AcceptedRateMhz)
                : firstMonotonicDeltaUs;
            MaxObservedEndUs = Math.Max(MaxObservedEndUs, endUs);
        }

        public RawTelemetryData BuildRaw()
        {
            if (!StreamDescriptors.TryGetValue(SstV5Constants.StreamTravel, out var travelDescriptor) ||
                (travelDescriptor.AcceptedSensorMask & (SstV5Constants.SensorForkTravel | SstV5Constants.SensorShockTravel)) == 0 ||
                travelDescriptor.AcceptedRateMhz == 0 ||
                travelDescriptor.AcceptedRateMhz % 1000 != 0 ||
                travelDescriptor.AcceptedRateMhz / 1000 > ushort.MaxValue)
            {
                throw Malformed(UnsupportedTravelMessage);
            }

            FrontBuilder.Finish();
            RearBuilder.Finish();

            var frontSegments = FrontBuilder.Segments.Select(segment => new RawCountSegment
            {
                FirstIndex = segment.FirstIndex,
                FirstMonotonicDeltaUs = segment.FirstMonotonicDeltaUs,
                Counts = segment.Values.ToArray(),
            }).ToArray();
            var rearSegments = RearBuilder.Segments.Select(segment => new RawCountSegment
            {
                FirstIndex = segment.FirstIndex,
                FirstMonotonicDeltaUs = segment.FirstMonotonicDeltaUs,
                Counts = segment.Values.ToArray(),
            }).ToArray();

            if (frontSegments.Length == 0 && rearSegments.Length == 0)
            {
                throw Malformed(UnsupportedTravelMessage);
            }

            var missingFinalStatus = FinalStatus is null;
            var malformedMessage = WarningMessage;
            if (missingFinalStatus)
            {
                malformedMessage ??= MissingFinalStatusMessage;
            }

            var durationSeconds = FinalStatus is not null
                ? FinalStatus.StoppedMonotonicDeltaUs / 1_000_000.0
                : MaxObservedEndUs / 1_000_000.0;

            var imuData = BuildImuData(this);
            var sampleRate = checked((ushort)(travelDescriptor.AcceptedRateMhz / 1000));
            return new RawTelemetryData
            {
                Magic = "SST5"u8.ToArray(),
                Version = Version,
                SampleRate = sampleRate,
                Timestamp = SessionStartUtcMs!.Value / 1000,
                SessionStartUtcMs = SessionStartUtcMs.Value,
                RecordingDurationSeconds = durationSeconds,
                Front = frontSegments.SelectMany(segment => segment.Counts).ToArray(),
                Rear = rearSegments.SelectMany(segment => segment.Counts).ToArray(),
                FrontSegments = frontSegments,
                RearSegments = rearSegments,
                StreamGaps = Gaps.ToArray(),
                FinalStatus = FinalStatus,
                MissingFinalStatus = missingFinalStatus,
                Markers = Markers.ToArray(),
                ImuData = imuData,
                GpsData = GpsRecords.Count > 0 ? GpsRecords.ToArray() : null,
                TemperatureData = TemperatureSamples.ToArray(),
                Malformed = malformedMessage is not null,
                MalformedMessage = malformedMessage,
            };
        }

        public void AddGap(byte streamKind, byte? locationId, ulong firstMissingIndex, ulong missingCount, ulong? missingTimeUs, string reason)
        {
            Gaps.Add(new RawStreamGap
            {
                StreamKind = streamKind,
                LocationId = locationId,
                FirstMissingIndex = firstMissingIndex,
                MissingCount = missingCount,
                MissingTimeUs = missingTimeUs,
                Reason = reason,
            });
        }

        public SstV5MalformedException Malformed(string message, Exception? innerException = null) =>
            new(
                message,
                StartTime(),
                Duration(),
                TelemetrySampleRate(),
                innerException);

        private DateTime? StartTime() => SessionStartUtcMs.HasValue
            ? DateTimeOffset.FromUnixTimeMilliseconds(SessionStartUtcMs.Value).LocalDateTime
            : null;

        private TimeSpan? Duration() => MaxObservedEndUs == 0
            ? null
            : TimeSpan.FromSeconds(MaxObservedEndUs / 1_000_000.0);

        private ushort? TelemetrySampleRate()
        {
            if (!StreamDescriptors.TryGetValue(SstV5Constants.StreamTravel, out var travelDescriptor) ||
                travelDescriptor.AcceptedRateMhz == 0 ||
                travelDescriptor.AcceptedRateMhz % 1000 != 0 ||
                travelDescriptor.AcceptedRateMhz / 1000 > ushort.MaxValue)
            {
                return null;
            }

            return checked((ushort)(travelDescriptor.AcceptedRateMhz / 1000));
        }
    }

    private sealed class FixedSegmentBuilder<T>(
        byte streamKind,
        byte? locationId,
        Action<byte, byte?, ulong, ulong, ulong?, string> addGap)
    {
        private readonly List<T> currentValues = [];
        private ulong currentFirstIndex;
        private ulong currentFirstMonotonicDeltaUs;
        private bool hasTimeline;
        private ulong lastIndex;

        public byte? LocationId => locationId;
        public List<FixedSegment<T>> Segments { get; } = [];

        public void AddValidSample(ulong index, ulong monotonicDeltaUs, T value, uint rateMhz)
        {
            var startsNewSegment = currentValues.Count == 0;

            // The first emitted sample establishes the timeline. Its logical index may be greater
            // than zero because the producer trims pre-anchor backlog at session start (per spec
            // that is not session loss), so it anchors the run at its own monotonic time without
            // recording a leading gap.
            //
            // Fixed-rate gaps are defined by missing logical sample indices, not by the batch
            // monotonic timestamp. Real producers stamp each batch from the monotonic clock, which
            // jitters by tens of microseconds around the nominal grid; contiguous indices mean no
            // samples were lost, so that jitter must not split the segment.
            if (hasTimeline && index != lastIndex + 1)
            {
                var missingCount = index > lastIndex ? index - lastIndex - 1 : 0;
                addGap(streamKind, locationId, lastIndex + 1, missingCount, RoundDurationUs(missingCount, rateMhz), "index_gap");
                Flush();
                startsNewSegment = true;
            }

            if (startsNewSegment)
            {
                currentFirstIndex = index;
                currentFirstMonotonicDeltaUs = monotonicDeltaUs;
            }

            currentValues.Add(value);
            hasTimeline = true;
            lastIndex = index;
        }

        public void AddInvalidRange(ulong firstIndex, ulong count, ulong firstMonotonicDeltaUs, uint rateMhz)
        {
            if (count == 0)
            {
                return;
            }

            addGap(streamKind, locationId, firstIndex, count, RoundDurationUs(count, rateMhz), "invalid_validity");
            Flush();
            hasTimeline = true;
            lastIndex = checked(firstIndex + count - 1);
        }

        public void Finish()
        {
            Flush();
        }

        private void Flush()
        {
            if (currentValues.Count == 0)
            {
                return;
            }

            Segments.Add(new FixedSegment<T>(
                currentFirstIndex,
                currentFirstMonotonicDeltaUs,
                [.. currentValues]));
            currentValues.Clear();
        }
    }

    private sealed record FixedSegment<T>(
        ulong FirstIndex,
        ulong FirstMonotonicDeltaUs,
        T[] Values);

    private sealed record V5StreamDescriptor(
        byte StreamKind,
        byte TimingModelId,
        byte SourceDescriptorCount,
        uint AcceptedSensorMask,
        uint AcceptedExtensionMask,
        uint AcceptedRateMhz,
        uint AcceptedBatchDurationMs,
        ushort CompactPayloadRecordBytes)
    {
        public byte GpsDriverId { get; set; }
        public byte GpsPayloadEncodingId { get; set; }
        public ushort GpsPayloadRecordBytes { get; set; }
        public ushort GpsExtensionRecordBytes { get; set; }
        public List<V5SourceDescriptor> Sources { get; } = [];
    }

    private sealed class V5SourceDescriptor
    {
        public byte StreamKind { get; set; }
        public byte DriverId { get; set; }
        public byte PayloadEncodingId { get; set; }
        public byte PayloadValueCount { get; set; }
        public uint SourceBitMask { get; set; }
        public ushort PayloadByteOffset { get; set; }
        public ushort PayloadRecordBytes { get; set; }
        public ushort PayloadValueWidthBits { get; set; }
        public ushort CountMin { get; set; }
        public ushort CountMax { get; set; }
        public ushort WrapModulus { get; set; }
        public byte Wraps { get; set; }
        public float AccelLsbPerG { get; set; }
        public float GyroLsbPerDps { get; set; }
        public byte BikeFrameId { get; set; }
        public float TemperatureLsbPerCelsius { get; set; }
        public float TemperatureCelsiusAtRawZero { get; set; }
    }

    private sealed class SstV5MalformedException(
        string message,
        DateTime? startTime,
        TimeSpan? duration,
        ushort? telemetrySampleRate,
        Exception? innerException = null) : Exception(message, innerException)
    {
        public DateTime? StartTime { get; } = startTime;
        public TimeSpan? Duration { get; } = duration;
        public ushort? TelemetrySampleRate { get; } = telemetrySampleRate;
    }
}

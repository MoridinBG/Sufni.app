namespace Sufni.Telemetry;

public class SstV5Parser : ISstParser
{
    private const string UnsupportedTravelMessage = "SST v5 travel data is missing or unsupported by this app.";
    private const string MissingFinalStatusMessage = "SST v5 final status is missing; parsed complete data chunks only.";
    private const string TrimmedTrailingChunkMessage = "SST v5 chunk extends past end of file; incomplete trailing chunk data was trimmed.";

    public SstFileInspection Inspect(BinaryReader reader, byte version)
    {
        var context = new V5InspectionContext(version);
        try
        {
            Scan(reader, context);
            return context.BuildInspection();
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

    private static void Scan(BinaryReader reader, V5InspectionContext context)
    {
        var stream = reader.BaseStream;
        var contentStart = stream.Position;
        if (stream.Length - stream.Position < SstV5ProtocolConstants.HeaderRemainderSize)
        {
            throw context.Malformed("SST v5 header is truncated.");
        }

        Span<byte> headerBytes = stackalloc byte[SstV5ProtocolConstants.HeaderRemainderSize];
        stream.ReadExactly(headerBytes);
        ParseHeader(headerBytes, context);

        Span<byte> envelopeBytes = stackalloc byte[SstV5ProtocolConstants.ChunkEnvelopeSize];
        while (stream.Position < stream.Length)
        {
            var chunkStart = stream.Position;
            if (stream.Length - stream.Position < SstV5ProtocolConstants.ChunkEnvelopeSize)
            {
                throw context.Malformed("SST v5 file ends with an incomplete chunk header.");
            }

            stream.ReadExactly(envelopeBytes);
            var envelope = ReadChunkEnvelope(envelopeBytes);
            var payloadStart = stream.Position;
            var declaredEnd = checked(payloadStart + envelope.DeclaredLength);

            if (declaredEnd > stream.Length)
            {
                if (context.MetadataParsed && context.HasCompleteTravelData)
                {
                    context.WarningMessage ??= TrimmedTrailingChunkMessage;
                    break;
                }

                throw context.Malformed("SST v5 chunk extends past end of file.");
            }

            ValidateChunkEnvelope(envelope, context);

            if (envelope.ChunkType == SstV5Constants.ChunkSessionMetadata)
            {
                if (context.MetadataParsed || chunkStart != contentStart + SstV5ProtocolConstants.HeaderRemainderSize)
                {
                    throw context.Malformed("SST v5 session metadata chunk is out of order.");
                }

                ParseMetadata(ReadPayload(reader, envelope.DeclaredLength), context);
                continue;
            }

            if (!context.MetadataParsed)
            {
                throw context.Malformed("SST v5 data chunk appears before session metadata.");
            }

            if (envelope.ChunkType == SstV5Constants.ChunkFinalStatus)
            {
                ScanFinalStatus(reader, envelope.DeclaredLength, context);
                continue;
            }

            ScanDataChunk(reader, envelope, context);
        }

        if (!context.MetadataParsed)
        {
            throw context.Malformed("SST v5 session metadata is missing.");
        }
    }

    private static void ScanDataChunk(BinaryReader reader, V5ChunkEnvelope envelope, V5InspectionContext context)
    {
        var streamKind = SstV5ProtocolConstants.StreamKindForDataChunk(envelope.ChunkType);
        var descriptor = RequireDataDescriptor(streamKind, context);
        var headerLength = checked((int)Math.Min(envelope.DeclaredLength, SstV5ProtocolConstants.DataHeaderSize));
        Span<byte> headerBytes = stackalloc byte[SstV5ProtocolConstants.DataHeaderSize];
        reader.BaseStream.ReadExactly(headerBytes[..headerLength]);

        SstV5DataHeader header;
        try
        {
            header = SstV5DescriptorReader.ReadDataHeader(headerBytes[..headerLength], envelope.DeclaredLength, descriptor);
        }
        catch (FormatException ex)
        {
            throw context.Malformed(ex.Message, ex);
        }

        SkipBytes(reader.BaseStream, envelope.DeclaredLength - (uint)headerLength);
        context.HasCompleteTravelData |= streamKind == SstV5Constants.StreamTravel;
        if (streamKind == SstV5Constants.StreamTravel)
        {
            uint describedSources = 0;
            for (var index = 0; index < descriptor.Sources.Count; index++)
            {
                describedSources |= descriptor.Sources[index].SourceBitMask;
            }

            context.HasProcessableTravelData |= (header.ValidityMask & describedSources) != 0;
        }

        context.ObserveDataEnd(descriptor, header.FirstMonotonicDeltaUs, header.SampleCount);
    }

    private static void ScanFinalStatus(BinaryReader reader, uint declaredLength, V5InspectionContext context)
    {
        var headerLength = checked((int)Math.Min(declaredLength, SstV5ProtocolConstants.FinalStatusHeaderSize));
        Span<byte> headerBytes = stackalloc byte[SstV5ProtocolConstants.FinalStatusHeaderSize];
        reader.BaseStream.ReadExactly(headerBytes[..headerLength]);
        var header = ReadFinalStatusHeader(headerBytes[..headerLength], declaredLength, context);
        SkipBytes(reader.BaseStream, declaredLength - (uint)headerLength);
        context.SetFinalStatus(new SstFinalStatus
        {
            SessionResultReason = header.SessionResultReason,
            StoppedMonotonicDeltaUs = header.StoppedMonotonicDeltaUs,
            Streams = [],
        });
    }

    private static byte[] ReadPayload(BinaryReader reader, uint declaredLength)
    {
        var payload = new byte[checked((int)declaredLength)];
        reader.BaseStream.ReadExactly(payload);
        return payload;
    }

    private static void SkipBytes(Stream stream, uint count)
    {
        stream.Seek(count, SeekOrigin.Current);
    }

    public RawTelemetryData Parse(BinaryReader reader, byte version)
    {
        var bytes = SstParserBytes.ReadRemainingBytes(reader);
        return Parse(bytes, version);
    }

    internal RawTelemetryData Parse(ReadOnlyMemory<byte> bytes, byte version)
    {
        try
        {
            return ParseBytes(bytes.Span, version);
        }
        catch (SstV5MalformedException ex)
        {
            throw new FormatException(ex.Message, ex);
        }
    }

    private static RawTelemetryData ParseBytes(ReadOnlySpan<byte> bytes, byte version)
    {
        var context = new V5ParseContext(version);
        if (bytes.Length < SstV5ProtocolConstants.HeaderRemainderSize)
        {
            throw context.Malformed("SST v5 header is truncated.");
        }

        var cursor = new SstByteReader(bytes);
        ParseHeader(cursor.ReadBytes(SstV5ProtocolConstants.HeaderRemainderSize), context);

        while (cursor.Position < cursor.Length)
        {
            if (cursor.Position + SstV5ProtocolConstants.ChunkEnvelopeSize > cursor.Length)
            {
                throw context.Malformed("SST v5 file ends with an incomplete chunk header.");
            }

            var chunkStart = cursor.Position;
            var envelope = ReadChunkEnvelope(cursor.ReadBytes(SstV5ProtocolConstants.ChunkEnvelopeSize));
            var chunkType = envelope.ChunkType;
            var declaredLength = envelope.DeclaredLength;
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

            var payloadLength = checked((int)declaredLength);
            var payload = bytes.Slice(payloadStart, payloadLength);
            cursor.MoveTo((int)declaredEnd);
            ValidateChunkEnvelope(envelope, context);

            if (chunkType == SstV5Constants.ChunkSessionMetadata)
            {
                if (context.MetadataParsed || chunkStart != SstV5ProtocolConstants.HeaderRemainderSize)
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

    private static void ParseHeader(ReadOnlySpan<byte> bytes, V5EnvelopeContext context)
    {
        var reader = new SstByteReader(bytes);
        var headerBytes = reader.ReadUInt16();
        var fileFlags = reader.ReadUInt16();
        context.SessionStartUtcMs = reader.ReadInt64();
        _ = reader.ReadUInt64(); // session_start_monotonic_us is the anchor; chunk deltas are already relative to it.

        if (headerBytes != SstV5ProtocolConstants.FixedHeaderBytes)
        {
            throw context.Malformed("SST v5 header length is invalid.");
        }

        if (fileFlags != 0)
        {
            throw context.Malformed("SST v5 file flags are invalid.");
        }
    }

    private static V5ChunkEnvelope ReadChunkEnvelope(ReadOnlySpan<byte> bytes)
    {
        var reader = new SstByteReader(bytes);
        return new V5ChunkEnvelope(reader.ReadUInt16(), reader.ReadUInt16(), reader.ReadUInt32());
    }

    private static void ValidateChunkEnvelope(V5ChunkEnvelope envelope, V5EnvelopeContext context)
    {
        if (envelope.Flags != 0)
        {
            throw context.Malformed("SST v5 chunk flags are invalid.");
        }

        if (!SstV5ProtocolConstants.IsKnownChunkType(envelope.ChunkType))
        {
            throw context.Malformed("SST v5 chunk type is unknown.");
        }

        if (context.FinalStatus is not null)
        {
            throw context.Malformed("SST v5 final status must be the last chunk.");
        }
    }

    private static void ParseMetadata(ReadOnlySpan<byte> payload, V5EnvelopeContext context)
    {
        try
        {
            var descriptor = SstV5DescriptorReader.ReadSessionDescriptor(payload);
            foreach (var streamDescriptor in descriptor.StreamDescriptors)
            {
                context.StreamDescriptors.Add(streamDescriptor.StreamKind, streamDescriptor);
                context.StreamDescriptorOrder.Add(streamDescriptor);
            }

            context.MetadataParsed = true;
            context.OnMetadataParsed();
        }
        catch (FormatException ex)
        {
            throw context.Malformed(ex.Message, ex);
        }
    }

    private static void ParseDataChunk(ushort chunkType, ReadOnlySpan<byte> payload, V5ParseContext context)
    {
        var streamKind = SstV5ProtocolConstants.StreamKindForDataChunk(chunkType);
        var descriptor = RequireDataDescriptor(streamKind, context);

        SstV5DataHeader header;
        try
        {
            header = SstV5DescriptorReader.ReadDataHeader(payload, descriptor);
        }
        catch (FormatException ex)
        {
            throw context.Malformed(ex.Message, ex);
        }

        var compactPayload = payload[SstV5ProtocolConstants.DataHeaderSize..];
        try
        {
            switch (streamKind)
            {
                case SstV5Constants.StreamTravel:
                    DecodeTravelData(descriptor, compactPayload, header, context);
                    break;
                case SstV5Constants.StreamImu:
                    DecodeImuData(descriptor, compactPayload, header, context);
                    break;
                case SstV5Constants.StreamTemperature:
                    DecodeTemperatureData(descriptor, compactPayload, header, context);
                    break;
                case SstV5Constants.StreamGps:
                    DecodeGpsData(descriptor, compactPayload, header, context);
                    break;
                case SstV5Constants.StreamBattery:
                    DecodeBatteryData(descriptor, compactPayload, header, context);
                    break;
                case SstV5Constants.StreamMarker:
                    DecodeMarkerData(descriptor, compactPayload, header, context);
                    break;
            }
        }
        catch (FormatException ex)
        {
            throw context.Malformed(ex.Message, ex);
        }

        context.HasCompleteTravelData |= streamKind == SstV5Constants.StreamTravel;
        context.ObserveDataEnd(descriptor, header.FirstMonotonicDeltaUs, header.SampleCount);
    }

    private static SstV5StreamDescriptor RequireDataDescriptor(byte streamKind, V5EnvelopeContext context)
    {
        if (context.StreamDescriptors.TryGetValue(streamKind, out var descriptor))
        {
            return descriptor;
        }

        throw context.Malformed("SST v5 data chunk references a stream without a descriptor.");
    }

    private static void DecodeTravelData(
        SstV5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        SstV5DataHeader header,
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

            if ((header.ValidityMask & source.SourceBitMask) == 0)
            {
                builder.AddInvalidRange(
                    header.FirstIndex,
                    header.SampleCount,
                    header.FirstMonotonicDeltaUs,
                    descriptor.AcceptedRateMhz);
            }
        }

        foreach (var record in SstV5CompactPayloadDecoder.DecodeTravel(descriptor, header, compactPayload))
        {
            var builder = record.SourceBitMask switch
            {
                SstV5Constants.SensorForkTravel => context.FrontBuilder,
                SstV5Constants.SensorShockTravel => context.RearBuilder,
                _ => throw context.Malformed("SST v5 travel source bit is unsupported by this app."),
            };
            builder.AddValidSample(record.Index, record.MonotonicDeltaUs, record.Count, descriptor.AcceptedRateMhz);
        }
    }

    private static void DecodeImuData(
        SstV5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        SstV5DataHeader header,
        V5ParseContext context)
    {
        foreach (var source in descriptor.Sources)
        {
            var location = SstV5ProtocolConstants.GetImuLocationId(source.SourceBitMask);
            var builder = context.ImuBuilders[location];

            if ((header.ValidityMask & source.SourceBitMask) == 0)
            {
                builder.AddInvalidRange(
                    header.FirstIndex,
                    header.SampleCount,
                    header.FirstMonotonicDeltaUs,
                    descriptor.AcceptedRateMhz);
            }
        }

        foreach (var record in SstV5CompactPayloadDecoder.DecodeImu(descriptor, header, compactPayload))
        {
            context.ImuBuilders[record.LocationId]
                .AddValidSample(record.Index, record.MonotonicDeltaUs, record.Record, descriptor.AcceptedRateMhz);
        }
    }

    private static void DecodeTemperatureData(
        SstV5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        SstV5DataHeader header,
        V5ParseContext context)
    {
        foreach (var source in descriptor.Sources)
        {
            if ((header.ValidityMask & source.SourceBitMask) == 0)
            {
                context.AddGap(
                    SstV5Constants.StreamTemperature,
                    SstV5ProtocolConstants.GetImuLocationId(source.SourceBitMask),
                    header.FirstIndex,
                    header.SampleCount,
                    null,
                    "invalid_validity");
            }
        }

        foreach (var record in SstV5CompactPayloadDecoder.DecodeTemperature(
                     descriptor,
                     header,
                     context.SessionStartUtcMs!.Value,
                     compactPayload))
        {
            context.TemperatureSamples.Add(record.Sample);
        }
    }

    private static void DecodeGpsData(
        SstV5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        SstV5DataHeader header,
        V5ParseContext context)
    {
        if ((header.ValidityMask & SstV5Constants.SensorGps) == 0)
        {
            context.AddGap(SstV5Constants.StreamGps, null, header.FirstIndex, header.SampleCount, null, "invalid_validity");
            return;
        }

        foreach (var record in SstV5CompactPayloadDecoder.DecodeGps(descriptor, header, compactPayload))
        {
            context.GpsRecords.Add(record.Record);
        }
    }

    private static void DecodeBatteryData(
        SstV5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        SstV5DataHeader header,
        V5ParseContext context)
    {
        if ((header.ValidityMask & SstV5Constants.SensorBattery) == 0)
        {
            context.AddGap(SstV5Constants.StreamBattery, null, header.FirstIndex, header.SampleCount, null, "invalid_validity");
            return;
        }

        // Battery records are decoded for protocol validation but intentionally not stored in app
        // data. Undefined battery flag bits are tolerated because a future appendix may define
        // additional bits.
        _ = SstV5CompactPayloadDecoder.DecodeBattery(descriptor, header, compactPayload);
    }

    private static void DecodeMarkerData(
        SstV5StreamDescriptor descriptor,
        ReadOnlySpan<byte> compactPayload,
        SstV5DataHeader header,
        V5ParseContext context)
    {
        foreach (var record in SstV5CompactPayloadDecoder.DecodeMarkers(descriptor, header, compactPayload))
        {
            if (record.MarkerType == SstV5Constants.MarkerManualUserMark)
            {
                context.Markers.Add(new MarkerData(record.MonotonicDeltaUs / 1_000_000.0));
            }
        }
    }

    private static void ParseFinalStatus(ReadOnlySpan<byte> payload, V5ParseContext context)
    {
        var header = ReadFinalStatusHeader(payload, (uint)payload.Length, context);
        var reader = new SstByteReader(payload);
        reader.MoveTo(SstV5ProtocolConstants.FinalStatusHeaderSize);

        var statuses = new SstStreamFinalStatus[header.StreamStatusCount];
        for (var index = 0; index < header.StreamStatusCount; index++)
        {
            var descriptor = context.StreamDescriptorOrder[index];
            var producerState = reader.ReadByte();
            var producerFailureReason = reader.ReadByte();
            var sinkBacklogBatches = reader.ReadUInt16();
            var producerMissedCount = reader.ReadUInt64();
            var producerMissingTimeUs = reader.ReadUInt64();
            var sinkMissedCount = reader.ReadUInt64();
            var sinkMissingTimeUs = reader.ReadUInt64();

            var status = new SstStreamFinalStatus
            {
                StreamKind = descriptor.StreamKind,
                ProducerState = producerState,
                ProducerFailureReason = producerFailureReason,
                SinkBacklogBatches = sinkBacklogBatches,
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

        context.SetFinalStatus(new SstFinalStatus
        {
            SessionResultReason = header.SessionResultReason,
            StoppedMonotonicDeltaUs = header.StoppedMonotonicDeltaUs,
            Streams = statuses,
        });
    }

    private static V5FinalStatusHeader ReadFinalStatusHeader(
        ReadOnlySpan<byte> payloadHeader,
        uint payloadLength,
        V5EnvelopeContext context)
    {
        if (payloadHeader.Length < SstV5ProtocolConstants.FinalStatusHeaderSize)
        {
            throw context.Malformed("SST v5 final status payload is truncated.");
        }

        var reader = new SstByteReader(payloadHeader);
        var sessionResultReason = reader.ReadByte();
        var streamStatusCount = reader.ReadByte();
        var finalFlags = reader.ReadUInt16();
        var stoppedMonotonicDeltaUs = reader.ReadUInt64();

        if (sessionResultReason is not 1 and not 2 and not 4 and not 5 and not 6 || finalFlags != 0)
        {
            throw context.Malformed("SST v5 final status header is invalid.");
        }

        if (streamStatusCount != context.StreamDescriptorOrder.Count ||
            payloadLength != SstV5ProtocolConstants.FinalStatusHeaderSize +
            streamStatusCount * SstV5ProtocolConstants.FinalStatusRecordSize)
        {
            throw context.Malformed("SST v5 final status payload length is invalid.");
        }

        return new V5FinalStatusHeader(sessionResultReason, streamStatusCount, stoppedMonotonicDeltaUs);
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

        RawImuDataSegmentHelper.FinalizeCanonicalSegments(context.ImuData, context.Gaps);
        return context.ImuData;
    }

    private static ulong? OptionalMissingTime(ulong missingTimeUs) => missingTimeUs == 0 ? null : missingTimeUs;

    private abstract class V5EnvelopeContext(byte version)
    {
        public byte Version { get; } = version;
        public long? SessionStartUtcMs { get; set; }
        public bool MetadataParsed { get; set; }
        public bool HasCompleteTravelData { get; set; }
        public ulong MaxObservedEndUs { get; set; }
        public string? WarningMessage { get; set; }
        public SstFinalStatus? FinalStatus { get; set; }
        public Dictionary<byte, SstV5StreamDescriptor> StreamDescriptors { get; } = [];
        public List<SstV5StreamDescriptor> StreamDescriptorOrder { get; } = [];

        public virtual void OnMetadataParsed()
        {
        }

        public void ObserveDataEnd(SstV5StreamDescriptor descriptor, ulong firstMonotonicDeltaUs, uint sampleCount)
        {
            var endUs = descriptor.TimingModelId == SstV5Constants.TimingFixedRate
                ? SstV5CompactPayloadDecoder.CalculateSampleMonotonicUs(
                    firstMonotonicDeltaUs,
                    sampleCount,
                    descriptor.AcceptedRateMhz)
                : firstMonotonicDeltaUs;
            MaxObservedEndUs = Math.Max(MaxObservedEndUs, endUs);
        }

        public void SetFinalStatus(SstFinalStatus finalStatus)
        {
            FinalStatus = finalStatus;
            MaxObservedEndUs = Math.Max(MaxObservedEndUs, finalStatus.StoppedMonotonicDeltaUs);
        }

        public SstV5StreamDescriptor RequireSupportedTravelDescriptor()
        {
            if (!StreamDescriptors.TryGetValue(SstV5Constants.StreamTravel, out var travelDescriptor) ||
                (travelDescriptor.AcceptedSensorMask & (SstV5Constants.SensorForkTravel | SstV5Constants.SensorShockTravel)) == 0 ||
                travelDescriptor.AcceptedRateMhz == 0 ||
                travelDescriptor.AcceptedRateMhz % 1000 != 0 ||
                travelDescriptor.AcceptedRateMhz / 1000 > ushort.MaxValue)
            {
                throw Malformed(UnsupportedTravelMessage);
            }

            return travelDescriptor;
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

    private sealed class V5InspectionContext(byte version) : V5EnvelopeContext(version)
    {
        public bool HasProcessableTravelData { get; set; }

        public ValidSstFileInspection BuildInspection()
        {
            var travelDescriptor = RequireSupportedTravelDescriptor();
            if (!HasProcessableTravelData)
            {
                throw Malformed(UnsupportedTravelMessage);
            }

            var malformedMessage = WarningMessage;
            if (FinalStatus is null)
            {
                malformedMessage ??= MissingFinalStatusMessage;
            }

            var durationSeconds = FinalStatus is not null
                ? FinalStatus.StoppedMonotonicDeltaUs / 1_000_000.0
                : MaxObservedEndUs / 1_000_000.0;
            return new ValidSstFileInspection(
                Version,
                DateTimeOffset.FromUnixTimeMilliseconds(SessionStartUtcMs!.Value).LocalDateTime,
                TimeSpan.FromSeconds(durationSeconds),
                checked((ushort)(travelDescriptor.AcceptedRateMhz / 1000)),
                HasUnknown: false,
                malformedMessage);
        }
    }

    private sealed class V5ParseContext : V5EnvelopeContext
    {
        public List<RawStreamGap> Gaps { get; } = [];
        public List<MarkerData> Markers { get; } = [];
        public RawImuData ImuData { get; } = new();
        public List<GpsRecord> GpsRecords { get; } = [];
        public List<TemperatureSample> TemperatureSamples { get; } = [];
        public FixedRateSegmentBuilder<ushort> FrontBuilder { get; }
        public FixedRateSegmentBuilder<ushort> RearBuilder { get; }
        public Dictionary<byte, FixedRateSegmentBuilder<ImuRecord>> ImuBuilders { get; } = [];

        public V5ParseContext(byte version) : base(version)
        {
            // Tag travel gaps with the travel sensor bit (fork=front, shock=rear) so the
            // processing layer can attribute a gap to the correct side instead of both.
            FrontBuilder = new FixedRateSegmentBuilder<ushort>(SstV5Constants.StreamTravel, (byte)SstV5Constants.SensorForkTravel, AddGap);
            RearBuilder = new FixedRateSegmentBuilder<ushort>(SstV5Constants.StreamTravel, (byte)SstV5Constants.SensorShockTravel, AddGap);
        }

        public override void OnMetadataParsed()
        {
            if (!StreamDescriptors.TryGetValue(SstV5Constants.StreamImu, out var imuDescriptor))
            {
                return;
            }

            foreach (var source in imuDescriptor.Sources.OrderBy(source => source.SourceBitMask))
            {
                var location = SstV5ProtocolConstants.GetImuLocationId(source.SourceBitMask);
                ImuData.Meta.Add(new ImuMetaEntry(location, source.AccelLsbPerG, source.GyroLsbPerDps));
                ImuData.ActiveLocations.Add(location);
                ImuBuilders[location] = new FixedRateSegmentBuilder<ImuRecord>(SstV5Constants.StreamImu, location, AddGap);
            }

            ImuData.SampleRate = imuDescriptor.AcceptedRateMhz % 1000 == 0
                ? checked((int)(imuDescriptor.AcceptedRateMhz / 1000))
                : 0;
        }

        public RawTelemetryData BuildRaw()
        {
            var travelDescriptor = RequireSupportedTravelDescriptor();

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

        public void AddGap(RawStreamGap gap)
        {
            Gaps.Add(gap);
        }

        public void AddGap(byte streamKind, byte? locationId, ulong firstMissingIndex, ulong missingCount, ulong? missingTimeUs, string reason)
        {
            AddGap(new RawStreamGap
            {
                StreamKind = streamKind,
                LocationId = locationId,
                FirstMissingIndex = firstMissingIndex,
                MissingCount = missingCount,
                MissingTimeUs = missingTimeUs,
                Reason = reason,
            });
        }

    }

    private readonly record struct V5ChunkEnvelope(ushort ChunkType, ushort Flags, uint DeclaredLength);

    private readonly record struct V5FinalStatusHeader(
        byte SessionResultReason,
        byte StreamStatusCount,
        ulong StoppedMonotonicDeltaUs);

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

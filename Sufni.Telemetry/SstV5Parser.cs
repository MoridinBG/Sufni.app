namespace Sufni.Telemetry;

public class SstV5Parser : ISstParser
{
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
        if (bytes.Length < SstV5ProtocolConstants.HeaderRemainderSize)
        {
            throw context.Malformed("SST v5 header is truncated.");
        }

        var cursor = new SstByteReader(bytes);
        var headerBytes = cursor.ReadUInt16();
        var fileFlags = cursor.ReadUInt16();
        var sessionStartUtcMs = cursor.ReadInt64();
        _ = cursor.ReadUInt64(); // session_start_monotonic_us is the anchor; chunk deltas are already relative to it.

        context.SessionStartUtcMs = sessionStartUtcMs;

        if (headerBytes != SstV5ProtocolConstants.FixedHeaderBytes)
        {
            throw context.Malformed("SST v5 header length is invalid.");
        }

        if (fileFlags != 0)
        {
            throw context.Malformed("SST v5 file flags are invalid.");
        }

        while (cursor.Position < cursor.Length)
        {
            if (cursor.Position + SstV5ProtocolConstants.ChunkEnvelopeSize > cursor.Length)
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

            if (!SstV5ProtocolConstants.IsKnownChunkType(chunkType))
            {
                throw context.Malformed("SST v5 chunk type is unknown.");
            }

            if (context.FinalStatus is not null)
            {
                throw context.Malformed("SST v5 final status must be the last chunk.");
            }

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

    private static void ParseMetadata(ReadOnlySpan<byte> payload, V5ParseContext context)
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
            context.InitializeImuMetadata();
        }
        catch (FormatException ex)
        {
            throw context.Malformed(ex.Message, ex);
        }
    }

    private static void ParseDataChunk(ushort chunkType, ReadOnlySpan<byte> payload, V5ParseContext context)
    {
        var streamKind = SstV5ProtocolConstants.StreamKindForDataChunk(chunkType);
        if (!context.StreamDescriptors.TryGetValue(streamKind, out var descriptor))
        {
            throw context.Malformed("SST v5 data chunk references a stream without a descriptor.");
        }

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
        if (payload.Length < SstV5ProtocolConstants.FinalStatusHeaderSize)
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
            payload.Length != SstV5ProtocolConstants.FinalStatusHeaderSize +
            streamStatusCount * SstV5ProtocolConstants.FinalStatusRecordSize)
        {
            throw context.Malformed("SST v5 final status payload length is invalid.");
        }

        var statuses = new SstStreamFinalStatus[streamStatusCount];
        for (var index = 0; index < streamStatusCount; index++)
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

        RawImuDataSegmentHelper.PopulateDenseRecordsFromAlignedSegments(context.ImuData, context.Gaps);
        return context.ImuData;
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
        public Dictionary<byte, SstV5StreamDescriptor> StreamDescriptors { get; } = [];
        public List<SstV5StreamDescriptor> StreamDescriptorOrder { get; } = [];
        public List<RawStreamGap> Gaps { get; } = [];
        public List<MarkerData> Markers { get; } = [];
        public RawImuData ImuData { get; } = new();
        public List<GpsRecord> GpsRecords { get; } = [];
        public List<TemperatureSample> TemperatureSamples { get; } = [];
        public FixedRateSegmentBuilder<ushort> FrontBuilder { get; }
        public FixedRateSegmentBuilder<ushort> RearBuilder { get; }
        public Dictionary<byte, FixedRateSegmentBuilder<ImuRecord>> ImuBuilders { get; } = [];

        public V5ParseContext(byte version)
        {
            Version = version;
            // Tag travel gaps with the travel sensor bit (fork=front, shock=rear) so the
            // processing layer can attribute a gap to the correct side instead of both.
            FrontBuilder = new FixedRateSegmentBuilder<ushort>(SstV5Constants.StreamTravel, (byte)SstV5Constants.SensorForkTravel, AddGap);
            RearBuilder = new FixedRateSegmentBuilder<ushort>(SstV5Constants.StreamTravel, (byte)SstV5Constants.SensorShockTravel, AddGap);
        }

        public void InitializeImuMetadata()
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

namespace Sufni.Telemetry;

public class SstV4TlvParser : ISstParser
{
    private const int HeaderPayloadSize = 12;
    private const int ChunkHeaderSize = 3;
    private const int RatesEntrySize = 3;
    private const int TelemetryRecordSize = 4;
    private const int MarkerChunkPayloadSize = 0;
    private const int ImuMetaCountSize = 1;
    private const int ImuMetaEntrySize = 9;
    private const int ImuRecordSize = 12;
    private const int GpsRecordSize = GpsBinaryRecordDecoder.RecordSize;
    private const int TemperatureRecordSize = 13;
    private const string TrimmedTrailingChunkMessage = "SST v4 chunk extends past end of file; incomplete trailing chunk data was trimmed.";

    public SstFileInspection Inspect(BinaryReader reader, byte version)
    {
        var bytes = SstParserBytes.ReadRemainingBytes(reader);
        if (bytes.Length < HeaderPayloadSize)
        {
            return new MalformedSstFileInspection(
                Version: version,
                StartTime: null,
                Duration: null,
                TelemetrySampleRate: null,
                Message: "SST v4 header is truncated.");
        }

        var cursor = new SstByteReader(bytes);
        _ = cursor.ReadUInt32();
        var timestamp = cursor.ReadInt64();

        ushort? sampleRate = null;
        long telemetrySamples = 0;
        var hasUnknown = false;
        string? malformedMessage = null;

        while (cursor.Position + ChunkHeaderSize <= cursor.Length)
        {
            var chunkStart = cursor.Position;
            var typeByte = cursor.ReadByte();
            var declaredLength = cursor.ReadUInt16();
            var bounds = ResolveChunkBounds(cursor.Length, chunkStart, declaredLength);
            var length = bounds.EffectivePayloadLength;
            var payload = bytes.AsSpan(chunkStart + ChunkHeaderSize, length);
            var payloadReader = new SstByteReader(payload);

            if (bounds.IsTrimmed)
            {
                malformedMessage ??= TrimmedTrailingChunkMessage;
            }

            if (!Enum.IsDefined(typeof(TlvChunkType), typeByte))
            {
                hasUnknown = true;
                cursor.MoveTo(bounds.End);
                continue;
            }

            var type = (TlvChunkType)typeByte;
            switch (type)
            {
                case TlvChunkType.Rates:
                    {
                        if (!TryGetUsablePayloadLength(length, RatesEntrySize, bounds.IsTrimmed, out var usableRatesLength))
                        {
                            return CreateMalformedInspection(
                                version,
                                timestamp,
                                sampleRate,
                                telemetrySamples,
                                "SST v4 rates chunk length is invalid.");
                        }

                        var entryCount = usableRatesLength / RatesEntrySize;
                        for (int i = 0; i < entryCount; i++)
                        {
                            var rateType = payloadReader.ReadByte();
                            var rate = payloadReader.ReadUInt16();
                            if (rateType == (byte)TlvChunkType.Telemetry)
                                sampleRate = rate;
                            else if (!Enum.IsDefined(typeof(TlvChunkType), rateType))
                                hasUnknown = true;
                        }
                        break;
                    }
                case TlvChunkType.Telemetry:
                    if (!TryGetUsablePayloadLength(length, TelemetryRecordSize, bounds.IsTrimmed, out var usableTelemetryLength))
                    {
                        return CreateMalformedInspection(
                            version,
                            timestamp,
                            sampleRate,
                            telemetrySamples,
                            "SST v4 telemetry chunk length is invalid.");
                    }

                    telemetrySamples += usableTelemetryLength / TelemetryRecordSize;
                    break;
                case TlvChunkType.Marker:
                    if (length != MarkerChunkPayloadSize && !bounds.IsTrimmed)
                    {
                        return CreateMalformedInspection(
                            version,
                            timestamp,
                            sampleRate,
                            telemetrySamples,
                            "SST v4 marker chunk length is invalid.");
                    }

                    break;
                case TlvChunkType.ImuMeta:
                    if (length < ImuMetaCountSize)
                    {
                        if (bounds.IsTrimmed)
                        {
                            break;
                        }

                        return CreateMalformedInspection(
                            version,
                            timestamp,
                            sampleRate,
                            telemetrySamples,
                            "SST v4 IMU metadata chunk length is invalid.");
                    }

                    var imuMetaCount = payloadReader.ReadByte();
                    if (length != ImuMetaCountSize + imuMetaCount * ImuMetaEntrySize && !bounds.IsTrimmed)
                    {
                        return CreateMalformedInspection(
                            version,
                            timestamp,
                            sampleRate,
                            telemetrySamples,
                            "SST v4 IMU metadata chunk length is invalid.");
                    }

                    break;
                case TlvChunkType.Imu:
                    if (!TryGetUsablePayloadLength(length, ImuRecordSize, bounds.IsTrimmed, out _))
                    {
                        return CreateMalformedInspection(
                            version,
                            timestamp,
                            sampleRate,
                            telemetrySamples,
                            "SST v4 IMU chunk length is invalid.");
                    }

                    break;
                case TlvChunkType.Gps:
                    if (!TryGetUsablePayloadLength(length, GpsRecordSize, bounds.IsTrimmed, out _))
                    {
                        return CreateMalformedInspection(
                            version,
                            timestamp,
                            sampleRate,
                            telemetrySamples,
                            "SST v4 GPS chunk length is invalid.");
                    }

                    break;
                case TlvChunkType.Temperature:
                    if (!TryGetUsablePayloadLength(length, TemperatureRecordSize, bounds.IsTrimmed, out _))
                    {
                        return CreateMalformedInspection(
                            version,
                            timestamp,
                            sampleRate,
                            telemetrySamples,
                            "SST v4 temperature chunk length is invalid.");
                    }

                    break;
            }

            cursor.MoveTo(bounds.End);
        }

        if (cursor.Position != cursor.Length)
        {
            return CreateMalformedInspection(
                version,
                timestamp,
                sampleRate,
                telemetrySamples,
                "SST v4 file ends with an incomplete chunk header.");
        }

        if (sampleRate is not > 0)
        {
            return CreateMalformedInspection(
                version,
                timestamp,
                sampleRate,
                telemetrySamples,
                "SST v4 telemetry sample rate is missing or invalid.");
        }

        var duration = TimeSpan.FromSeconds((double)telemetrySamples / sampleRate.Value);
        var startTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;
        return new ValidSstFileInspection(version, startTime, duration, sampleRate.Value, hasUnknown, malformedMessage);
    }

    public RawTelemetryData Parse(BinaryReader reader, byte version)
    {
        var bytes = SstParserBytes.ReadRemainingBytes(reader);
        if (bytes.Length < HeaderPayloadSize)
            throw new FormatException("SST v4 header is truncated.");

        var cursor = new SstByteReader(bytes);
        _ = cursor.ReadUInt32(); // Padding
        var timestamp = cursor.ReadInt64();

        var frontList = new List<ushort>();
        var rearList = new List<ushort>();
        var markers = new List<MarkerData>();
        RawImuData? imuData = null;
        var gpsRecords = new List<GpsRecord>();
        var temperatureSamples = new List<TemperatureSample>();
        var rates = new Dictionary<TlvChunkType, ushort>();
        var telemetrySampleCount = 0;
        string? malformedMessage = null;

        while (cursor.Position + ChunkHeaderSize <= cursor.Length)
        {
            var chunkStart = cursor.Position;
            var typeByte = cursor.ReadByte();
            var declaredLength = cursor.ReadUInt16();
            var bounds = ResolveChunkBounds(cursor.Length, chunkStart, declaredLength);
            var length = bounds.EffectivePayloadLength;
            var payload = bytes.AsSpan(chunkStart + ChunkHeaderSize, length);
            var payloadReader = new SstByteReader(payload);

            if (bounds.IsTrimmed)
                malformedMessage ??= TrimmedTrailingChunkMessage;

            if (!Enum.IsDefined(typeof(TlvChunkType), typeByte))
            {
                cursor.MoveTo(bounds.End);
                continue;
            }

            var type = (TlvChunkType)typeByte;

            switch (type)
            {
                case TlvChunkType.Rates:
                    if (!TryGetUsablePayloadLength(length, RatesEntrySize, bounds.IsTrimmed, out var usableRatesLength))
                        throw new FormatException("SST v4 rates chunk length is invalid.");

                    var entryCount = usableRatesLength / RatesEntrySize;
                    for (int i = 0; i < entryCount; i++)
                    {
                        var rTypeByte = payloadReader.ReadByte();
                        var rRate = payloadReader.ReadUInt16();
                        if (Enum.IsDefined(typeof(TlvChunkType), rTypeByte))
                        {
                            rates[(TlvChunkType)rTypeByte] = rRate;
                        }
                    }
                    break;

                case TlvChunkType.Telemetry:
                    if (!TryGetUsablePayloadLength(length, TelemetryRecordSize, bounds.IsTrimmed, out var usableTelemetryLength))
                        throw new FormatException("SST v4 telemetry chunk length is invalid.");

                    var recordCount = usableTelemetryLength / TelemetryRecordSize;
                    for (int i = 0; i < recordCount; i++)
                    {
                        frontList.Add(payloadReader.ReadUInt16());
                        rearList.Add(payloadReader.ReadUInt16());
                        telemetrySampleCount++;
                    }
                    break;

                case TlvChunkType.Marker:
                    if (length != MarkerChunkPayloadSize && !bounds.IsTrimmed)
                        throw new FormatException("SST v4 marker chunk length is invalid.");

                    if (rates.TryGetValue(TlvChunkType.Telemetry, out var tRate) && tRate > 0)
                    {
                        markers.Add(new MarkerData((double)telemetrySampleCount / tRate));
                    }
                    break;

                case TlvChunkType.ImuMeta:
                    if (length < ImuMetaCountSize)
                    {
                        if (bounds.IsTrimmed)
                            break;

                        throw new FormatException("SST v4 IMU metadata chunk length is invalid.");
                    }

                    imuData = imuData ?? new RawImuData();
                    var imuMetaCount = payloadReader.ReadByte();
                    var requiredImuMetaLength = ImuMetaCountSize + imuMetaCount * ImuMetaEntrySize;
                    if (length < requiredImuMetaLength && bounds.IsTrimmed)
                    {
                        break;
                    }

                    if (length != requiredImuMetaLength && !bounds.IsTrimmed)
                        throw new FormatException("SST v4 IMU metadata chunk length is invalid.");

                    imuData.SampleRate = rates.GetValueOrDefault(TlvChunkType.Imu, (ushort)0);
                    for (int i = 0; i < imuMetaCount; i++)
                    {
                        var locId = payloadReader.ReadByte();
                        var accelLsb = payloadReader.ReadSingle();
                        var gyroLsb = payloadReader.ReadSingle();
                        imuData.Meta.Add(new ImuMetaEntry(locId, accelLsb, gyroLsb));
                        imuData.ActiveLocations.Add(locId);
                    }
                    break;

                case TlvChunkType.Imu:
                    if (!TryGetUsablePayloadLength(length, ImuRecordSize, bounds.IsTrimmed, out var usableImuLength))
                        throw new FormatException("SST v4 IMU chunk length is invalid.");

                    imuData = imuData ?? new RawImuData();
                    var imuRecordCount = usableImuLength / ImuRecordSize;
                    for (int i = 0; i < imuRecordCount; i++)
                    {
                        var ax = payloadReader.ReadInt16();
                        var ay = payloadReader.ReadInt16();
                        var az = payloadReader.ReadInt16();
                        var gx = payloadReader.ReadInt16();
                        var gy = payloadReader.ReadInt16();
                        var gz = payloadReader.ReadInt16();
                        imuData.Records.Add(new ImuRecord(ax, ay, az, gx, gy, gz));
                    }
                    break;

                case TlvChunkType.Gps:
                    if (!TryGetUsablePayloadLength(length, GpsRecordSize, bounds.IsTrimmed, out var usableGpsLength))
                        throw new FormatException("SST v4 GPS chunk length is invalid.");

                    var gpsRecordCount = usableGpsLength / GpsRecordSize;
                    for (int i = 0; i < gpsRecordCount; i++)
                    {
                        var record = GpsBinaryRecordDecoder.Decode(payloadReader.ReadBytes(GpsRecordSize));
                        if (record is not null)
                            gpsRecords.Add(record);
                    }
                    break;

                case TlvChunkType.Temperature:
                    if (!TryGetUsablePayloadLength(length, TemperatureRecordSize, bounds.IsTrimmed, out var usableTemperatureLength))
                        throw new FormatException("SST v4 temperature chunk length is invalid.");

                    var temperatureRecordCount = usableTemperatureLength / TemperatureRecordSize;
                    for (int i = 0; i < temperatureRecordCount; i++)
                    {
                        var sampleTimestamp = payloadReader.ReadInt64();
                        var locationId = payloadReader.ReadByte();
                        var temperatureCelsius = payloadReader.ReadSingle();
                        temperatureSamples.Add(new TemperatureSample(sampleTimestamp, locationId, temperatureCelsius));
                    }
                    break;

            }

            // Ensure we're at the exact chunk boundary regardless of how many bytes the handler read
            cursor.MoveTo(bounds.End);
        }

        if (cursor.Position != cursor.Length)
            throw new FormatException("SST v4 file ends with an incomplete chunk header.");

        var front = frontList.ToArray();
        var rear = rearList.ToArray();
        var sampleRate = rates.GetValueOrDefault(TlvChunkType.Telemetry, (ushort)0);

        if (sampleRate == 0)
            throw new FormatException("SST v4 telemetry sample rate is missing or invalid.");

        if (front.Length == 0 && rear.Length == 0)
            throw new FormatException("SST v4 telemetry data is missing.");

        var rtd = new RawTelemetryData
        {
            Magic = "SST"u8.ToArray(),
            Version = version,
            SampleRate = sampleRate,
            Timestamp = timestamp,
            Markers = markers.ToArray(),
            ImuData = imuData is { Meta.Count: > 0 } ? imuData : null,
            GpsData = gpsRecords.Count > 0 ? gpsRecords.ToArray() : null,
            TemperatureData = temperatureSamples.ToArray(),
            Malformed = malformedMessage is not null,
            MalformedMessage = malformedMessage
        };

        if (front.Length > 0)
        {
            rtd.Front = front;
        }

        if (rear.Length > 0)
        {
            rtd.Rear = rear;
        }

        return rtd;
    }

    private static ChunkBounds ResolveChunkBounds(int dataLength, int chunkStart, ushort declaredPayloadLength)
    {
        var declaredEnd = chunkStart + ChunkHeaderSize + declaredPayloadLength;
        if (declaredEnd <= dataLength)
        {
            return new ChunkBounds(declaredPayloadLength, declaredEnd, IsTrimmed: false);
        }

        var payloadStart = chunkStart + ChunkHeaderSize;
        var availablePayloadLength = Math.Max(0, dataLength - payloadStart);
        return new ChunkBounds(availablePayloadLength, dataLength, IsTrimmed: true);
    }

    private static bool TryGetUsablePayloadLength(
        int payloadLength,
        int recordSize,
        bool allowTrim,
        out int usablePayloadLength)
    {
        var remainder = payloadLength % recordSize;
        if (remainder == 0)
        {
            usablePayloadLength = payloadLength;
            return true;
        }

        if (allowTrim)
        {
            usablePayloadLength = payloadLength - remainder;
            return true;
        }

        usablePayloadLength = 0;
        return false;
    }

    private readonly record struct ChunkBounds(
        int EffectivePayloadLength,
        int End,
        bool IsTrimmed);

    private static MalformedSstFileInspection CreateMalformedInspection(
        byte version,
        long? timestamp,
        ushort? sampleRate,
        long telemetrySamples,
        string message)
    {
        DateTime? startTime = null;
        if (timestamp.HasValue)
        {
            startTime = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value).LocalDateTime;
        }

        TimeSpan? duration = null;
        if (sampleRate is > 0)
        {
            duration = TimeSpan.FromSeconds((double)telemetrySamples / sampleRate.Value);
        }

        return new MalformedSstFileInspection(
            Version: version,
            StartTime: startTime,
            Duration: duration,
            TelemetrySampleRate: sampleRate,
            Message: message);
    }
}

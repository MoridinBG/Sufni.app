namespace Sufni.Telemetry;

public class SstV4TlvParser : ISstParser
{
    private const int HeaderPayloadSize = 12;
    private const int HeaderSize = 16;
    private const int ChunkHeaderSize = 3;
    private const int RatesEntrySize = 3;
    private const int TelemetryRecordSize = 4;
    private const int MarkerChunkPayloadSize = 0;
    private const int ImuMetaCountSize = 1;
    private const int ImuMetaEntrySize = 9;
    private const int ImuRecordSize = 12;
    private const int GpsRecordSize = 46;
    private const int SignedEncoderThreshold = 2048;
    private const int SignedEncoderRange = 4096;

    public SstFileInspection Inspect(BinaryReader reader, byte version)
    {
        var stream = reader.BaseStream;
        if (stream.Length - stream.Position < HeaderPayloadSize)
        {
            return new MalformedSstFileInspection(
                Version: version,
                StartTime: null,
                Duration: null,
                TelemetrySampleRate: null,
                Message: "SST v4 header is truncated.");
        }

        _ = reader.ReadUInt32();
        var timestamp = reader.ReadInt64();

        ushort? sampleRate = null;
        long telemetrySamples = 0;
        var hasUnknown = false;

        while (stream.Position + ChunkHeaderSize <= stream.Length)
        {
            var chunkStart = stream.Position;
            var typeByte = reader.ReadByte();
            var length = reader.ReadUInt16();
            var chunkEnd = chunkStart + ChunkHeaderSize + length;

            if (chunkEnd > stream.Length)
            {
                return CreateMalformedInspection(
                    version,
                    timestamp,
                    sampleRate,
                    telemetrySamples,
                    "SST v4 chunk extends past end of file.");
            }

            if (!Enum.IsDefined(typeof(TlvChunkType), typeByte))
            {
                hasUnknown = true;
                stream.Position = chunkEnd;
                continue;
            }

            var type = (TlvChunkType)typeByte;
            switch (type)
            {
                case TlvChunkType.Rates:
                    {
                        if (length % RatesEntrySize != 0)
                        {
                            return CreateMalformedInspection(
                                version,
                                timestamp,
                                sampleRate,
                                telemetrySamples,
                                "SST v4 rates chunk length is invalid.");
                        }

                        var entryCount = length / RatesEntrySize;
                        for (int i = 0; i < entryCount; i++)
                        {
                            var rateType = reader.ReadByte();
                            var rate = reader.ReadUInt16();
                            if (rateType == (byte)TlvChunkType.Telemetry)
                                sampleRate = rate;
                            else if (!Enum.IsDefined(typeof(TlvChunkType), rateType))
                                hasUnknown = true;
                        }
                        break;
                    }
                case TlvChunkType.Telemetry:
                    if (length % TelemetryRecordSize != 0)
                    {
                        return CreateMalformedInspection(
                            version,
                            timestamp,
                            sampleRate,
                            telemetrySamples,
                            "SST v4 telemetry chunk length is invalid.");
                    }

                    telemetrySamples += length / TelemetryRecordSize;
                    break;
                case TlvChunkType.Marker:
                    if (length != MarkerChunkPayloadSize)
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
                        return CreateMalformedInspection(
                            version,
                            timestamp,
                            sampleRate,
                            telemetrySamples,
                            "SST v4 IMU metadata chunk length is invalid.");
                    }

                    var imuMetaCount = reader.ReadByte();
                    if (length != ImuMetaCountSize + imuMetaCount * ImuMetaEntrySize)
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
                    if (length % ImuRecordSize != 0)
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
                    if (length % GpsRecordSize != 0)
                    {
                        return CreateMalformedInspection(
                            version,
                            timestamp,
                            sampleRate,
                            telemetrySamples,
                            "SST v4 GPS chunk length is invalid.");
                    }

                    break;
            }

            stream.Position = chunkEnd;
        }

        if (stream.Position != stream.Length)
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
        return new ValidSstFileInspection(version, startTime, duration, sampleRate.Value, hasUnknown);
    }

    public RawTelemetryData Parse(BinaryReader reader, byte version)
    {
        var stream = reader.BaseStream;
        if (stream.Length - stream.Position < HeaderPayloadSize)
            throw new FormatException("SST v4 header is truncated.");

        _ = reader.ReadUInt32(); // Padding
        var timestamp = (int)reader.ReadInt64();

        var frontList = new List<int>();
        var rearList = new List<int>();
        var markers = new List<MarkerData>();
        RawImuData? imuData = null;
        var gpsRecords = new List<GpsRecord>();
        var rates = new Dictionary<TlvChunkType, ushort>();
        var telemetrySampleCount = 0;

        while (stream.Position + ChunkHeaderSize <= stream.Length)
        {
            var chunkStart = stream.Position;
            var typeByte = reader.ReadByte();
            var length = reader.ReadUInt16();
            var chunkEnd = chunkStart + ChunkHeaderSize + length;

            if (chunkEnd > stream.Length)
                throw new FormatException("SST v4 chunk extends past end of file.");

            if (!Enum.IsDefined(typeof(TlvChunkType), typeByte))
            {
                stream.Position = chunkEnd;
                continue;
            }

            var type = (TlvChunkType)typeByte;

            switch (type)
            {
                case TlvChunkType.Rates:
                    if (length % RatesEntrySize != 0)
                        throw new FormatException("SST v4 rates chunk length is invalid.");

                    var entryCount = length / RatesEntrySize;
                    for (int i = 0; i < entryCount; i++)
                    {
                        var rTypeByte = reader.ReadByte();
                        var rRate = reader.ReadUInt16();
                        if (Enum.IsDefined(typeof(TlvChunkType), rTypeByte))
                        {
                            rates[(TlvChunkType)rTypeByte] = rRate;
                        }
                    }
                    break;

                case TlvChunkType.Telemetry:
                    if (length % TelemetryRecordSize != 0)
                        throw new FormatException("SST v4 telemetry chunk length is invalid.");

                    var recordCount = length / TelemetryRecordSize;
                    for (int i = 0; i < recordCount; i++)
                    {
                        var f = (int)reader.ReadUInt16();
                        var r = (int)reader.ReadUInt16();
                        if (f >= SignedEncoderThreshold) f -= SignedEncoderRange;
                        if (r >= SignedEncoderThreshold) r -= SignedEncoderRange;
                        frontList.Add(f);
                        rearList.Add(r);
                        telemetrySampleCount++;
                    }
                    break;

                case TlvChunkType.Marker:
                    if (length != MarkerChunkPayloadSize)
                        throw new FormatException("SST v4 marker chunk length is invalid.");

                    if (rates.TryGetValue(TlvChunkType.Telemetry, out var tRate) && tRate > 0)
                    {
                        markers.Add(new MarkerData((double)telemetrySampleCount / tRate));
                    }
                    break;

                case TlvChunkType.ImuMeta:
                    if (length < ImuMetaCountSize)
                        throw new FormatException("SST v4 IMU metadata chunk length is invalid.");

                    imuData = imuData ?? new RawImuData();
                    var imuMetaCount = reader.ReadByte();
                    if (length != ImuMetaCountSize + imuMetaCount * ImuMetaEntrySize)
                        throw new FormatException("SST v4 IMU metadata chunk length is invalid.");

                    imuData.SampleRate = rates.GetValueOrDefault(TlvChunkType.Imu, (ushort)0);
                    for (int i = 0; i < imuMetaCount; i++)
                    {
                        var locId = reader.ReadByte();
                        var accelLsb = reader.ReadSingle();
                        var gyroLsb = reader.ReadSingle();
                        imuData.Meta.Add(new ImuMetaEntry(locId, accelLsb, gyroLsb));
                        imuData.ActiveLocations.Add(locId);
                    }
                    break;

                case TlvChunkType.Imu:
                    if (length % ImuRecordSize != 0)
                        throw new FormatException("SST v4 IMU chunk length is invalid.");

                    imuData = imuData ?? new RawImuData();
                    var imuRecordCount = length / ImuRecordSize;
                    for (int i = 0; i < imuRecordCount; i++)
                    {
                        var ax = reader.ReadInt16();
                        var ay = reader.ReadInt16();
                        var az = reader.ReadInt16();
                        var gx = reader.ReadInt16();
                        var gy = reader.ReadInt16();
                        var gz = reader.ReadInt16();
                        imuData.Records.Add(new ImuRecord(ax, ay, az, gx, gy, gz));
                    }
                    break;

                case TlvChunkType.Gps:
                    if (length % GpsRecordSize != 0)
                        throw new FormatException("SST v4 GPS chunk length is invalid.");

                    var gpsRecordCount = length / GpsRecordSize;
                    for (int i = 0; i < gpsRecordCount; i++)
                    {
                        var date = reader.ReadUInt32();
                        var timeMs = reader.ReadUInt32();
                        var latitude = reader.ReadDouble();
                        var longitude = reader.ReadDouble();
                        var altitude = reader.ReadSingle();
                        var speed = reader.ReadSingle();
                        var heading = reader.ReadSingle();
                        var fixMode = reader.ReadByte();
                        var satellites = reader.ReadByte();
                        var epe2d = reader.ReadSingle();
                        var epe3d = reader.ReadSingle();

                        var year = (int)(date / 10000);
                        var month = (int)(date / 100 % 100);
                        var day = (int)(date % 100);

                        if (year < 1 || year > 9999 || month < 1 || month > 12 || day < 1 || day > 31)
                        {
                            continue;
                        }

                        var utcTimestamp = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc)
                            .AddMilliseconds(timeMs);

                        gpsRecords.Add(new GpsRecord(
                            utcTimestamp, latitude, longitude, altitude,
                            speed, heading, fixMode, satellites, epe2d, epe3d));
                    }
                    break;

            }

            // Ensure we're at the exact chunk boundary regardless of how many bytes the handler read
            stream.Position = chunkEnd;
        }

        if (stream.Position != stream.Length)
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
            Malformed = false
        };

        if (front.Length > 0)
        {
            var (fixedFront, frontAnomalyCount) = SpikeElimination.EliminateSpikes(front);
            rtd.Front = fixedFront;
            rtd.FrontAnomalyRate = (double)frontAnomalyCount / rtd.Front.Length * rtd.SampleRate;
        }

        if (rear.Length > 0)
        {
            var (fixedRear, rearAnomalyCount) = SpikeElimination.EliminateSpikes(rear);
            rtd.Rear = fixedRear;
            rtd.RearAnomalyRate = (double)rearAnomalyCount / rtd.Rear.Length * rtd.SampleRate;
        }

        return rtd;
    }

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

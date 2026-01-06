namespace Sufni.Telemetry;

public class SstV4TlvParser : ISstParser
{
    public static TimeSpan ParseDuration(BinaryReader reader)
    {
        ushort sampleRate = 0;
        long telemetrySamples = 0;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            var type = reader.ReadByte();
            var length = reader.ReadUInt16();

            if (type == (byte)TlvChunkType.Rates)
            {
                var entryCount = length / 3;
                for (int i = 0; i < entryCount; i++)
                {
                    var rType = reader.ReadByte();
                    var rRate = reader.ReadUInt16();
                    if (rType == (byte)TlvChunkType.Telemetry)
                        sampleRate = rRate;
                }
            }
            else if (type == (byte)TlvChunkType.Telemetry)
            {
                telemetrySamples += length / 4;
                reader.BaseStream.Seek(length, SeekOrigin.Current);
            }
            else
            {
                reader.BaseStream.Seek(length, SeekOrigin.Current);
            }
        }

        return sampleRate > 0
            ? TimeSpan.FromSeconds((double)telemetrySamples / sampleRate)
            : TimeSpan.Zero;
    }

    public RawTelemetryData Parse(BinaryReader reader, byte version)
    {
        _ = reader.ReadUInt32(); // Padding
        var timestamp = (int)reader.ReadInt64();

        var frontList = new List<int>();
        var rearList = new List<int>();
        var markers = new List<MarkerData>();
        RawImuData? imuData = null;
        var gpsRecords = new List<GpsRecord>();
        var rates = new Dictionary<TlvChunkType, ushort>();
        
        var stream = reader.BaseStream;
        var telemetrySampleCount = 0;

        while (stream.Position < stream.Length)
        {
            var typeByte = reader.ReadByte();
            var length = reader.ReadUInt16();

            if (!Enum.IsDefined(typeof(TlvChunkType), typeByte))
            {
                stream.Seek(length, SeekOrigin.Current);
                continue;
            }

            var type = (TlvChunkType)typeByte;

            switch (type)
            {
                case TlvChunkType.Rates:
                    var entryCount = length / 3;
                    for (int i = 0; i < entryCount; i++)
                    {
                        var rType = (TlvChunkType)reader.ReadByte();
                        var rRate = reader.ReadUInt16();
                        rates[rType] = rRate;
                    }
                    break;

                case TlvChunkType.Telemetry:
                    var recordCount = length / 4;
                    for (int i = 0; i < recordCount; i++)
                    {
                        var f = (int)reader.ReadUInt16();
                        var r = (int)reader.ReadUInt16();
                        if (f >= 2048) f -= 4096;
                        if (r >= 2048) r -= 4096;
                        frontList.Add(f);
                        rearList.Add(r);
                        telemetrySampleCount++;
                    }
                    break;

                case TlvChunkType.Marker:
                    if (rates.TryGetValue(TlvChunkType.Telemetry, out var tRate) && tRate > 0)
                    {
                        markers.Add(new MarkerData((double)telemetrySampleCount / tRate));
                    }
                    break;

                case TlvChunkType.ImuMeta:
                    // Imu Meta should be only one and before Imu data, but just in case...
                    imuData = imuData ?? new RawImuData();
                    
                    var imuMetaCount = reader.ReadByte();
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
                    imuData = imuData ?? new RawImuData();
                    var imuRecordCount = length / 12;
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
                    var gpsRecordCount = length / 46;
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
                        var utcTimestamp = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc)
                            .AddMilliseconds(timeMs);

                        gpsRecords.Add(new GpsRecord(
                            utcTimestamp, latitude, longitude, altitude,
                            speed, heading, fixMode, satellites, epe2d, epe3d));
                    }
                    break;

                default:
                    stream.Seek(length, SeekOrigin.Current);
                    break;
            }
        }

        var front = frontList.ToArray();
        var rear = rearList.ToArray();
        var sampleRate = rates.GetValueOrDefault(TlvChunkType.Telemetry, (ushort)0);

        var rtd = new RawTelemetryData
        {
            Magic = "SST"u8.ToArray(),
            Version = version,
            SampleRate = sampleRate,
            Timestamp = timestamp,
            Markers = markers.ToArray(),
            ImuData = imuData is { Meta.Count: > 0 } ? imuData : null,
            GpsData = gpsRecords.Count > 0 ? gpsRecords.ToArray() : null
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
}

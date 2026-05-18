namespace Sufni.Telemetry;

public class SstV3Parser : ISstParser
{
    private const int HeaderPayloadSize = 12;
    private const int TelemetryRecordSize = 4;

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
                Message: "SST v3 header is truncated.");
        }

        var cursor = new SstByteReader(bytes);
        var sampleRate = cursor.ReadUInt16();
        _ = cursor.ReadUInt16();
        var timestamp = cursor.ReadInt64();
        var startTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;

        var payloadLength = bytes.Length - HeaderPayloadSize;
        if (payloadLength % TelemetryRecordSize != 0)
        {
            return new MalformedSstFileInspection(
                Version: version,
                StartTime: startTime,
                Duration: null,
                TelemetrySampleRate: sampleRate,
                Message: "SST v3 telemetry payload length is invalid.");
        }

        if (sampleRate == 0)
        {
            return new MalformedSstFileInspection(
                Version: version,
                StartTime: startTime,
                Duration: null,
                TelemetrySampleRate: sampleRate,
                Message: "SST v3 telemetry sample rate is invalid.");
        }

        var count = payloadLength / TelemetryRecordSize;
        var duration = TimeSpan.FromSeconds((double)count / sampleRate);
        return new ValidSstFileInspection(version, startTime, duration, sampleRate, false);
    }

    public RawTelemetryData Parse(BinaryReader reader, byte version)
    {
        var bytes = SstParserBytes.ReadRemainingBytes(reader);
        if (bytes.Length < HeaderPayloadSize)
            throw new FormatException("SST v3 header is truncated.");

        var cursor = new SstByteReader(bytes);
        var sampleRate = cursor.ReadUInt16();
        _ = cursor.ReadUInt16(); // padding
        var timestamp = cursor.ReadInt64();

        var payloadLength = bytes.Length - HeaderPayloadSize;
        if (payloadLength % TelemetryRecordSize != 0)
            throw new FormatException("SST v3 telemetry payload length is invalid.");

        if (sampleRate == 0)
            throw new FormatException("SST v3 telemetry sample rate is invalid.");

        var count = (int)(payloadLength / TelemetryRecordSize);

        var front = new ushort[count];
        var rear = new ushort[count];
        var frontPresent = count > 0;
        var rearPresent = count > 0;
        for (var i = 0; i < count; i++)
        {
            var rawFront = cursor.ReadUInt16();
            var rawRear = cursor.ReadUInt16();

            if (i == 0)
            {
                frontPresent = rawFront != ushort.MaxValue;
                rearPresent = rawRear != ushort.MaxValue;
            }

            if (frontPresent) front[i] = rawFront;
            if (rearPresent) rear[i] = rawRear;
        }

        var rtd = new RawTelemetryData
        {
            Magic = "SST"u8.ToArray(),
            Version = version,
            SampleRate = sampleRate,
            Timestamp = timestamp,
            Markers = []
        };

        if (frontPresent)
        {
            rtd.Front = front;
        }

        if (rearPresent)
        {
            rtd.Rear = rear;
        }

        return rtd;
    }
}

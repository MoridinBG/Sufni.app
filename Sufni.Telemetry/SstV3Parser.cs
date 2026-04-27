namespace Sufni.Telemetry;

public class SstV3Parser : ISstParser
{
    private const int HeaderPayloadSize = 12;
    private const int HeaderSize = 16;
    private const int TelemetryRecordSize = 4;

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
                Message: "SST v3 header is truncated.");
        }

        var sampleRate = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        var timestamp = reader.ReadInt64();
        var startTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;

        var payloadLength = stream.Length - HeaderSize;
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
        var stream = reader.BaseStream;
        if (stream.Length - stream.Position < HeaderPayloadSize)
            throw new FormatException("SST v3 header is truncated.");

        var sampleRate = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // padding
        var timestamp = reader.ReadInt64();

        var payloadLength = stream.Length - HeaderSize;
        if (payloadLength % TelemetryRecordSize != 0)
            throw new FormatException("SST v3 telemetry payload length is invalid.");

        if (sampleRate == 0)
            throw new FormatException("SST v3 telemetry sample rate is invalid.");

        var count = (int)(payloadLength / TelemetryRecordSize);

        var front = new int[count];
        var rear = new int[count];
        var frontPresent = count > 0;
        var rearPresent = count > 0;
        for (var i = 0; i < count; i++)
        {
            var rawFront = reader.ReadUInt16();
            var rawRear = reader.ReadUInt16();

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
            var (fixedFront, frontAnomalyCount) = SpikeElimination.EliminateSpikes(front);
            rtd.Front = fixedFront;
            rtd.FrontAnomalyRate = (double)frontAnomalyCount / rtd.Front.Length * rtd.SampleRate;
        }

        if (rearPresent)
        {
            var (fixedRear, rearAnomalyCount) = SpikeElimination.EliminateSpikes(rear);
            rtd.Rear = fixedRear;
            rtd.RearAnomalyRate = (double)rearAnomalyCount / rtd.Rear.Length * rtd.SampleRate;
        }

        return rtd;
    }
}

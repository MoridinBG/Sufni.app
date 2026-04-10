namespace Sufni.Telemetry;

public class SstV3Parser : ISstParser
{
    private const int HeaderSize = 16;

    public SstFileInspection Inspect(BinaryReader reader, byte version)
    {
        var stream = reader.BaseStream;
        if (stream.Length - stream.Position < HeaderSize - 4)
        {
            return new MalformedSstFileInspection(
                Version: version,
                StartTime: null,
                Duration: null,
                TelemetrySampleRate: null,
                HasUnknown: false,
                Message: "SST v3 header is truncated.");
        }

        var sampleRate = reader.ReadUInt16();
        _ = reader.ReadUInt16();
        var timestamp = reader.ReadInt64();
        var startTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime;

        var payloadLength = stream.Length - HeaderSize;
        if (payloadLength < 0)
        {
            return new MalformedSstFileInspection(
                Version: version,
                StartTime: startTime,
                Duration: null,
                TelemetrySampleRate: sampleRate,
                HasUnknown: false,
                Message: "SST v3 telemetry payload is truncated.");
        }

        if (payloadLength % 4 != 0)
        {
            return new MalformedSstFileInspection(
                Version: version,
                StartTime: startTime,
                Duration: null,
                TelemetrySampleRate: sampleRate,
                HasUnknown: false,
                Message: "SST v3 telemetry payload length is invalid.");
        }

        if (sampleRate == 0)
        {
            return new MalformedSstFileInspection(
                Version: version,
                StartTime: startTime,
                Duration: null,
                TelemetrySampleRate: sampleRate,
                HasUnknown: false,
                Message: "SST v3 telemetry sample rate is invalid.");
        }

        var count = payloadLength / 4;
        var duration = TimeSpan.FromSeconds((double)count / sampleRate);
        return new ValidSstFileInspection(version, startTime, duration, sampleRate, false);
    }

    public RawTelemetryData Parse(BinaryReader reader, byte version)
    {
        var stream = reader.BaseStream;
        if (stream.Length - stream.Position < HeaderSize - 4)
            throw new FormatException("SST v3 header is truncated.");

        var sampleRate = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // padding
        var timestamp = (int)reader.ReadInt64();

        var payloadLength = stream.Length - HeaderSize;
        if (payloadLength < 0 || payloadLength % 4 != 0)
            throw new FormatException("SST v3 telemetry payload length is invalid.");

        if (sampleRate == 0)
            throw new FormatException("SST v3 telemetry sample rate is invalid.");

        var count = (int)(payloadLength / 4);

        var front = new int[count];
        var rear = new int[count];
        for (var i = 0; i < count; i++)
        {
            var f = (int)reader.ReadUInt16();
            var r = (int)reader.ReadUInt16();
            if (f >= 2048) f -= 4096;
            if (r >= 2048) r -= 4096;
            front[i] = f;
            rear[i] = r;
        }

        var rtd = new RawTelemetryData
        {
            Magic = "SST"u8.ToArray(),
            Version = version,
            SampleRate = sampleRate,
            Timestamp = timestamp,
            Markers = []
        };

        if (front.Length > 0 && front[0] != 0xffff)
        {
            var (fixedFront, frontAnomalyCount) = SpikeElimination.EliminateSpikes(front);
            rtd.Front = fixedFront;
            rtd.FrontAnomalyRate = (double)frontAnomalyCount / rtd.Front.Length * rtd.SampleRate;
        }

        if (rear.Length > 0 && rear[0] != 0xffff)
        {
            var (fixedRear, rearAnomalyCount) = SpikeElimination.EliminateSpikes(rear);
            rtd.Rear = fixedRear;
            rtd.RearAnomalyRate = (double)rearAnomalyCount / rtd.Rear.Length * rtd.SampleRate;
        }

        return rtd;
    }
}

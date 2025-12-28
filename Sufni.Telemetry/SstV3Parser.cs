namespace Sufni.Telemetry;

public class SstV3Parser : ISstParser
{
    public RawTelemetryData Parse(BinaryReader reader, byte version)
    {
        var sampleRate = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // padding
        var timestamp = (int)reader.ReadInt64();

        var stream = reader.BaseStream;
        var count = ((int)stream.Length - 16) / 4;

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

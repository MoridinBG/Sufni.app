using System.Text;

namespace Sufni.Telemetry;

public record Record(ushort ForkAngle, ushort ShockAngle);

public class RawTelemetryData
{
    public byte[] Magic { get; private set; } = null!;
    public byte Version { get; private set; }
    public ushort SampleRate { get; private set; }
    public int Timestamp { get; private set; }
    public ushort[] Front { get; private set; } = null!;
    public ushort[] Rear { get; private set; } = null!;

    public static RawTelemetryData FromStream(Stream stream)
    {
        using var reader = new BinaryReader(stream);

        var rtd = new RawTelemetryData();

        rtd.Magic = reader.ReadBytes(3);
        rtd.Version = reader.ReadByte();
        rtd.SampleRate = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // padding
        rtd.Timestamp = (int)reader.ReadInt64();

        if (Encoding.ASCII.GetString(rtd.Magic) != "SST")
        {
            throw new Exception("Data is not SST format");
        }

        var count = ((int)stream.Length - 16) / 4;
        var records = new List<Record>(count);

        for (var i = 0; i < count; i++)
        {
            records.Add(new Record(
                reader.ReadUInt16(),
                reader.ReadUInt16()));
        }

        var hasFront = records[0].ForkAngle != 0xffff;
        var hasRear = records[0].ShockAngle != 0xffff;

        ushort frontError = 0, rearError = 0;
        ushort frontBaseline = records[0].ForkAngle, rearBaseline = records[0].ShockAngle;

        foreach (var r in records.Skip(1))
        {
            if (r.ForkAngle <= frontBaseline) continue;
            if (r.ForkAngle - frontBaseline > 0x0050)
            {
                frontError = r.ForkAngle;
            }

            break;
        }

        foreach (var r in records.Skip(1))
        {
            if (r.ShockAngle <= rearBaseline) continue;
            if (r.ShockAngle - rearBaseline > 0x0050)
            {
                rearError = r.ShockAngle;
            }

            break;
        }

        var front = new List<ushort>();
        var rear = new List<ushort>();
        foreach (var r in records)
        {
            if (hasFront)
            {
                front.Add((ushort)(r.ForkAngle - frontError));
            }

            if (hasRear)
            {
                rear.Add((ushort)(r.ShockAngle - rearError));
            }
        }

        rtd.Front = [.. front];
        rtd.Rear = [.. rear];

        return rtd;
    }

    public static RawTelemetryData FromByteArray(byte[] bytes)
    {
        return FromStream(new MemoryStream(bytes));
    }
}
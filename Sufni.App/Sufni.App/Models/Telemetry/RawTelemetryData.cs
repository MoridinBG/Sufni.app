using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sufni.App.Models.Telemetry;

public record Record(ushort ForkAngle, ushort ShockAngle);

public class RawTelemetryData
{
    public byte[] Magic { get; }
    public byte Version { get; }
    public ushort SampleRate { get; }
    public int Timestamp { get; }
    public ushort[] Front { get; }
    public ushort[] Rear { get; }

    public RawTelemetryData(Stream stream)
    {
        using var reader = new BinaryReader(stream);

        Magic = reader.ReadBytes(3);
        Version = reader.ReadByte();
        SampleRate = reader.ReadUInt16();
        _ = reader.ReadUInt16(); // padding
        Timestamp = (int)reader.ReadInt64();

        if (Encoding.ASCII.GetString(Magic) != "SST")
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

        Front = [.. front];
        Rear = [.. rear];
    }

    public RawTelemetryData(byte[] sstData) : this(new MemoryStream(sstData))
    {
    }
}
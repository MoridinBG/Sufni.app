using System.Text;
using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

internal static class SstTestFiles
{
    public static MemoryStream CreateV3Stream(
        ushort sampleRate = 1000,
        long timestamp = 123456789,
        params (ushort Front, ushort Rear)[] samples) =>
        new(CreateV3(sampleRate, timestamp, samples));

    public static MemoryStream CreateV3ParserStream(
        ushort sampleRate = 1000,
        long timestamp = 123456789,
        params (ushort Front, ushort Rear)[] samples)
    {
        var stream = CreateV3Stream(sampleRate, timestamp, samples);
        stream.Position = 4;
        return stream;
    }

    public static byte[] CreateV3(
        ushort sampleRate = 1000,
        long timestamp = 123456789,
        params (ushort Front, ushort Rear)[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)3);
        writer.Write(sampleRate);
        writer.Write((ushort)0);
        writer.Write(timestamp);

        foreach (var (front, rear) in samples)
        {
            writer.Write(front);
            writer.Write(rear);
        }

        return stream.ToArray();
    }

    public static MemoryStream CreateV4Stream(long timestamp = 123456789, params Action<BinaryWriter>[] chunks) =>
        new(CreateV4(timestamp, chunks));

    public static MemoryStream CreateV4ParserStream(long timestamp = 123456789, params Action<BinaryWriter>[] chunks)
    {
        var stream = CreateV4Stream(timestamp, chunks);
        stream.Position = 4;
        return stream;
    }

    public static byte[] CreateV4(long timestamp = 123456789, params Action<BinaryWriter>[] chunks)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0);
        writer.Write(timestamp);

        foreach (var chunk in chunks)
        {
            chunk(writer);
        }

        return stream.ToArray();
    }

    public static Action<BinaryWriter> Rates(params (TlvChunkType Type, ushort SampleRate)[] rates)
    {
        return writer =>
        {
            writer.Write((byte)TlvChunkType.Rates);
            writer.Write((ushort)(rates.Length * 3));
            foreach (var (type, sampleRate) in rates)
            {
                writer.Write((byte)type);
                writer.Write(sampleRate);
            }
        };
    }

    public static Action<BinaryWriter> Telemetry(params (ushort Front, ushort Rear)[] samples) =>
        Chunk(TlvChunkType.Telemetry, TelemetryPayload(samples));

    public static byte[] TelemetryPayload(params (ushort Front, ushort Rear)[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        foreach (var (front, rear) in samples)
        {
            writer.Write(front);
            writer.Write(rear);
        }

        return stream.ToArray();
    }

    public static Action<BinaryWriter> Marker() =>
        Chunk(TlvChunkType.Marker, []);

    public static Action<BinaryWriter> ImuMeta(params ImuMetaSpec[] entries)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                payload.Write((byte)entries.Length);
                foreach (var entry in entries)
                {
                    payload.Write(entry.Location);
                    payload.Write(entry.AccelLsbPerG);
                    payload.Write(entry.GyroLsbPerDps);
                }
            }

            WriteChunk(writer, TlvChunkType.ImuMeta, stream.ToArray());
        };
    }

    public static Action<BinaryWriter> Imu(params ImuRecordSpec[] records)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                foreach (var record in records)
                {
                    payload.Write(record.Ax);
                    payload.Write(record.Ay);
                    payload.Write(record.Az);
                    payload.Write(record.Gx);
                    payload.Write(record.Gy);
                    payload.Write(record.Gz);
                }
            }

            WriteChunk(writer, TlvChunkType.Imu, stream.ToArray());
        };
    }

    public static Action<BinaryWriter> Temperature(params TemperatureSpec[] records)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                foreach (var record in records)
                {
                    payload.Write(record.TimestampUtc);
                    payload.Write(record.LocationId);
                    payload.Write(record.TemperatureCelsius);
                }
            }

            WriteChunk(writer, TlvChunkType.Temperature, stream.ToArray());
        };
    }

    public static Action<BinaryWriter> Gps(params GpsRecordSpec[] records)
    {
        return writer =>
        {
            using var stream = new MemoryStream();
            using (var payload = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                foreach (var record in records)
                {
                    WriteGpsRecord(payload, record);
                }
            }

            WriteChunk(writer, TlvChunkType.Gps, stream.ToArray());
        };
    }

    public static Action<BinaryWriter> Chunk(TlvChunkType type, byte[] payload, ushort? declaredLength = null) =>
        writer => WriteChunk(writer, type, payload, declaredLength);

    public static Action<BinaryWriter> Chunk(byte type, byte[] payload, ushort? declaredLength = null) =>
        writer => WriteChunk(writer, type, payload, declaredLength);

    private static void WriteChunk(BinaryWriter writer, TlvChunkType type, byte[] payload, ushort? declaredLength = null) =>
        WriteChunk(writer, (byte)type, payload, declaredLength);

    private static void WriteChunk(BinaryWriter writer, byte type, byte[] payload, ushort? declaredLength = null)
    {
        writer.Write(type);
        writer.Write(declaredLength ?? (ushort)payload.Length);
        writer.Write(payload);
    }

    private static void WriteGpsRecord(BinaryWriter writer, GpsRecordSpec record)
    {
        writer.Write(record.Date);
        writer.Write(record.TimeMs);
        writer.Write(record.Latitude);
        writer.Write(record.Longitude);
        writer.Write(record.Altitude);
        writer.Write(record.Speed);
        writer.Write(record.Heading);
        writer.Write(record.FixMode);
        writer.Write(record.Satellites);
        writer.Write(record.Epe2d);
        writer.Write(record.Epe3d);
    }
}

internal readonly record struct ImuMetaSpec(
    byte Location,
    float AccelLsbPerG,
    float GyroLsbPerDps);

internal readonly record struct ImuRecordSpec(
    short Ax,
    short Ay,
    short Az,
    short Gx,
    short Gy,
    short Gz);

internal readonly record struct TemperatureSpec(
    long TimestampUtc,
    byte LocationId,
    float TemperatureCelsius);

internal readonly record struct GpsRecordSpec(
    uint Date,
    uint TimeMs,
    double Latitude,
    double Longitude,
    float Altitude = 150.5f,
    float Speed = 25.3f,
    float Heading = 180.0f,
    byte FixMode = 2,
    byte Satellites = 12,
    float Epe2d = 2.5f,
    float Epe3d = 3.5f);

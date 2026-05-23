using System.Text;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Infrastructure;

public static class TestSstFiles
{
    public static byte[] CreateValidV3(
        ushort sampleRate = 1000,
        long timestamp = 1_700_000_000,
        int sampleCount = 256)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteV3Header(writer, sampleRate, timestamp);
        for (var index = 0; index < sampleCount; index++)
        {
            var front = 1900.0 + 260.0 * Math.Sin(index * 2.0 * Math.PI / 48.0);
            var rear = 1850.0 + 240.0 * Math.Sin(index * 2.0 * Math.PI / 52.0);
            writer.Write((ushort)Math.Round(front));
            writer.Write((ushort)Math.Round(rear));
        }

        return stream.ToArray();
    }

    public static byte[] CreateV3WithFrontOnly(
        ushort sampleRate = 100,
        long timestamp = 1_700_000_000,
        ushort firstSample = 1200,
        int sampleCount = 64)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteV3Header(writer, sampleRate, timestamp);
        for (ushort sample = 0; sample < sampleCount; sample++)
        {
            writer.Write((ushort)(firstSample + sample));
            writer.Write(ushort.MaxValue);
        }

        return stream.ToArray();
    }

    public static byte[] CreateValidV4WithUnknownChunk(int telemetrySampleCount)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteV4Header(writer);
        WriteRatesChunk(writer, telemetrySampleRate: 1000);
        WriteChunk(writer, 0xFF, [1, 2]);
        WriteTelemetryChunk(writer, telemetrySampleCount);

        return stream.ToArray();
    }

    public static byte[] CreateMalformedV4WithInvalidTelemetryLength()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteV4Header(writer);
        WriteRatesChunk(writer, telemetrySampleRate: 1000);
        WriteChunk(writer, TlvChunkType.Telemetry, [1, 2, 3, 4, 5]);

        return stream.ToArray();
    }

    public static byte[] CreateV4WithTelemetryChunkExtendingPastEnd(int telemetrySampleCount)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteV4Header(writer);
        WriteRatesChunk(writer, telemetrySampleRate: 1000);

        var actualTelemetryPayloadLength = telemetrySampleCount * 4;
        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)(actualTelemetryPayloadLength + 4));
        WriteTelemetrySamples(writer, telemetrySampleCount);

        return stream.ToArray();
    }

    private static void WriteV3Header(BinaryWriter writer, ushort sampleRate, long timestamp)
    {
        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)3);
        writer.Write(sampleRate);
        writer.Write((ushort)0);
        writer.Write(timestamp);
    }

    private static void WriteV4Header(BinaryWriter writer, long timestamp = 123456789)
    {
        writer.Write(Encoding.ASCII.GetBytes("SST"));
        writer.Write((byte)4);
        writer.Write((uint)0);
        writer.Write(timestamp);
    }

    private static void WriteRatesChunk(BinaryWriter writer, ushort telemetrySampleRate)
    {
        writer.Write((byte)TlvChunkType.Rates);
        writer.Write((ushort)3);
        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write(telemetrySampleRate);
    }

    private static void WriteTelemetryChunk(BinaryWriter writer, int sampleCount)
    {
        writer.Write((byte)TlvChunkType.Telemetry);
        writer.Write((ushort)(sampleCount * 4));
        WriteTelemetrySamples(writer, sampleCount);
    }

    private static void WriteTelemetrySamples(BinaryWriter writer, int sampleCount)
    {
        for (var i = 0; i < sampleCount; i++)
        {
            writer.Write((ushort)500);
            writer.Write((ushort)600);
        }
    }

    private static void WriteChunk(BinaryWriter writer, TlvChunkType type, byte[] payload) =>
        WriteChunk(writer, (byte)type, payload);

    private static void WriteChunk(BinaryWriter writer, byte type, byte[] payload)
    {
        writer.Write(type);
        writer.Write((ushort)payload.Length);
        writer.Write(payload);
    }
}

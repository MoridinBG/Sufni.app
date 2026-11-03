using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.TestSupport.Fixtures;

public static class TestSstFiles
{
    public static byte[] CreateValidV3(
        ushort sampleRate = 1000,
        long timestamp = 1_700_000_000,
        int sampleCount = 256) =>
        SstTestFiles.CreateV3(sampleRate, timestamp, Enumerable.Range(0, sampleCount)
            .Select(index => (
                (ushort)Math.Round(1900.0 + 260.0 * Math.Sin(index * 2.0 * Math.PI / 48.0)),
                (ushort)Math.Round(1850.0 + 240.0 * Math.Sin(index * 2.0 * Math.PI / 52.0))))
            .ToArray());

    public static byte[] CreateV3WithFrontOnly(
        ushort sampleRate = 100,
        long timestamp = 1_700_000_000,
        ushort firstSample = 1200,
        int sampleCount = 64) =>
        SstTestFiles.CreateV3(sampleRate, timestamp, Enumerable.Range(0, sampleCount)
            .Select(index => ((ushort)(firstSample + index), ushort.MaxValue)).ToArray());

    public static byte[] CreateValidV4WithUnknownChunk(int telemetrySampleCount) =>
        SstTestFiles.CreateV4(chunks:
        [
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Chunk(0xFF, [1, 2]),
            SstTestFiles.Telemetry(TelemetrySamples(telemetrySampleCount)),
        ]);

    public static byte[] CreateMalformedV4WithInvalidTelemetryLength() =>
        SstTestFiles.CreateV4(chunks:
        [
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Chunk(TlvChunkType.Telemetry, [1, 2, 3, 4, 5]),
        ]);

    public static byte[] CreateV4WithTelemetryChunkExtendingPastEnd(int telemetrySampleCount) =>
        SstTestFiles.CreateV4(chunks:
        [
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Chunk(TlvChunkType.Telemetry,
                SstTestFiles.TelemetryPayload(TelemetrySamples(telemetrySampleCount)),
                (ushort)(telemetrySampleCount * 4 + 4)),
        ]);

    private static (ushort Front, ushort Rear)[] TelemetrySamples(int count) =>
        Enumerable.Repeat(((ushort)500, (ushort)600), count).ToArray();
}

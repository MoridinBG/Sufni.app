using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class SstV4TlvParserTests
{
    [Fact]
    public void Parse_ValidV4File_AllChunks_ReturnsCorrectData()
    {
        using var ms = SstTestFiles.CreateV4ParserStream(
            123456789,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000), (TlvChunkType.Imu, 100)),
            SstTestFiles.Telemetry((1000, 2000), (1100, 2100)),
            SstTestFiles.Marker(),
            SstTestFiles.ImuMeta(new ImuMetaSpec(0, 16384.0f, 16.4f)),
            SstTestFiles.Imu(new ImuRecordSpec(100, 200, 300, 10, 20, 30)));
        using var reader = new BinaryReader(ms);

        var parser = new SstV4TlvParser();

        // Act
        var result = parser.Parse(reader, 4);

        // Assert
        Assert.Equal(4, result.Version);
        Assert.Equal(1000, result.SampleRate);
        Assert.Equal(123456789, result.Timestamp);
        Assert.Equal(2, result.Front.Length);
        Assert.Equal(2, result.Rear.Length);
        Assert.Single(result.Markers);
        Assert.Equal(2.0 / 1000.0, result.Markers[0].TimestampOffset);
        Assert.NotNull(result.ImuData);
        Assert.Single(result.ImuData.Meta);
        Assert.Single(result.ImuData.Records);
        Assert.Equal(100, result.ImuData.Records[0].Ax);
    }

    [Fact]
    public void Parse_TemperatureChunk_ReturnsTemperatureSamples()
    {
        using var ms = SstTestFiles.CreateV4ParserStream(
            123456789,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Telemetry((500, 600)),
            SstTestFiles.Temperature(
                new TemperatureSpec(123456790, 0, 22.5f),
                new TemperatureSpec(123456791, 2, 26.75f)));
        using var reader = new BinaryReader(ms);

        var result = new SstV4TlvParser().Parse(reader, 4);

        Assert.Equal(2, result.TemperatureData.Length);
        Assert.Equal(123456790, result.TemperatureData[0].TimestampUtc);
        Assert.Equal(0, result.TemperatureData[0].LocationId);
        Assert.Equal(22.5f, result.TemperatureData[0].TemperatureCelsius);
        Assert.Equal(123456791, result.TemperatureData[1].TimestampUtc);
        Assert.Equal(2, result.TemperatureData[1].LocationId);
        Assert.Equal(26.75f, result.TemperatureData[1].TemperatureCelsius);
    }

    [Fact]
    public void Parse_GpsChunk_ReturnsCorrectData()
    {
        using var ms = SstTestFiles.CreateV4ParserStream(
            0,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Telemetry((500, 600)),
            SstTestFiles.Gps(new GpsRecordSpec(
                Date: 20250106,
                TimeMs: 43200000,
                Latitude: 47.123456,
                Longitude: 19.654321)));
        using var reader = new BinaryReader(ms);

        var parser = new SstV4TlvParser();

        // Act
        var result = parser.Parse(reader, 4);

        // Assert
        Assert.NotNull(result.GpsData);
        Assert.Single(result.GpsData);
        var gps = result.GpsData[0];
        Assert.Equal(new DateTime(2025, 1, 6, 12, 0, 0, DateTimeKind.Utc), gps.Timestamp);
        Assert.Equal(47.123456, gps.Latitude, 6);
        Assert.Equal(19.654321, gps.Longitude, 6);
        Assert.Equal(150.5f, gps.Altitude);
        Assert.Equal(25.3f, gps.Speed);
        Assert.Equal(180.0f, gps.Heading);
        Assert.Equal(2, gps.FixMode);
        Assert.Equal(12, gps.Satellites);
        Assert.Equal(2.5f, gps.Epe2d);
        Assert.Equal(3.5f, gps.Epe3d);
    }

    [Fact]
    public void Parse_GpsChunk_SkipsInvalidGpsDateRecords()
    {
        using var ms = SstTestFiles.CreateV4Stream(
            0,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Telemetry((500, 600)),
            SstTestFiles.Gps(
                new GpsRecordSpec(20250229, 0, 1, 2),
                new GpsRecordSpec(20240229, 1_000, 47.123456, 19.654321)));

        var result = RawTelemetryData.FromStream(ms);

        var gps = Assert.Single(result.GpsData!);
        Assert.Equal(new DateTime(2024, 2, 29, 0, 0, 1, DateTimeKind.Utc), gps.Timestamp);
        Assert.Equal(47.123456, gps.Latitude, 6);
        Assert.Equal(19.654321, gps.Longitude, 6);
    }

    [Fact]
    public void Parse_UnknownChunkType_SkipsGracefully()
    {
        using var ms = SstTestFiles.CreateV4ParserStream(
            0,
            SstTestFiles.Chunk(0xFF, [1, 2, 3, 4, 5]),
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Telemetry((500, 600)));
        using var reader = new BinaryReader(ms);

        var parser = new SstV4TlvParser();

        // Act
        var result = parser.Parse(reader, 4);

        // Assert
        Assert.Single(result.Front);
        Assert.Equal(500, result.Front[0]);
    }

    [Fact]
    public void Inspect_UnknownChunkType_ReturnsValidInspectionWithHasUnknown()
    {
        using var ms = SstTestFiles.CreateV4Stream(
            123456789,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Chunk(0xFF, [1, 2, 3, 4, 5]),
            SstTestFiles.Telemetry((500, 600)));

        var result = RawTelemetryData.InspectStream(ms);

        var inspection = Assert.IsType<ValidSstFileInspection>(result);
        Assert.True(inspection.HasUnknown);
        Assert.Equal(4, inspection.Version);
        Assert.Equal(1000, inspection.TelemetrySampleRate);
        Assert.Equal(TimeSpan.FromSeconds(1.0 / 1000.0), inspection.Duration);
    }

    [Fact]
    public void Inspect_InvalidTelemetryChunkLength_ReturnsMalformedInspection()
    {
        using var ms = SstTestFiles.CreateV4Stream(
            123456789,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Chunk(TlvChunkType.Telemetry, [1, 2, 3, 4, 5]));

        var result = RawTelemetryData.InspectStream(ms);

        var inspection = Assert.IsType<MalformedSstFileInspection>(result);
        Assert.Equal((byte)4, inspection.Version);
        Assert.NotNull(inspection.StartTime);
        Assert.NotEmpty(inspection.Message);
    }

    [Fact]
    public void Parse_InvalidTelemetryChunkLength_ThrowsFormatException()
    {
        using var ms = SstTestFiles.CreateV4Stream(
            123456789,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Chunk(TlvChunkType.Telemetry, [1, 2, 3, 4, 5]));

        Assert.Throws<FormatException>(() => RawTelemetryData.FromStream(ms));
    }

    [Fact]
    public void Inspect_InvalidTemperatureChunkLength_ReturnsMalformedInspection()
    {
        using var ms = SstTestFiles.CreateV4Stream(
            123456789,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Telemetry((500, 600)),
            SstTestFiles.Chunk(TlvChunkType.Temperature, new byte[12]));

        var result = RawTelemetryData.InspectStream(ms);

        var inspection = Assert.IsType<MalformedSstFileInspection>(result);
        Assert.Equal((byte)4, inspection.Version);
        Assert.NotNull(inspection.StartTime);
        Assert.NotEmpty(inspection.Message);
    }

    [Fact]
    public void Parse_InvalidTemperatureChunkLength_ThrowsFormatException()
    {
        using var ms = SstTestFiles.CreateV4Stream(
            123456789,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Telemetry((500, 600)),
            SstTestFiles.Chunk(TlvChunkType.Temperature, new byte[12]));

        Assert.Throws<FormatException>(() => RawTelemetryData.FromStream(ms));
    }

    [Fact]
    public void Inspect_TelemetryChunkExtendingPastEnd_ReturnsImportableMalformedInspection()
    {
        using var ms = CreateV4WithTelemetryChunkExtendingPastEnd(actualTelemetryPayloadBytes: 10, declaredTelemetryPayloadBytes: 16);

        var result = RawTelemetryData.InspectStream(ms);

        var inspection = Assert.IsType<ValidSstFileInspection>(result);
        Assert.Equal((byte)4, inspection.Version);
        Assert.Equal(1000, inspection.TelemetrySampleRate);
        Assert.Equal(TimeSpan.FromSeconds(2.0 / 1000.0), inspection.Duration);
        Assert.NotNull(inspection.MalformedMessage);
    }

    [Fact]
    public void Parse_TelemetryChunkExtendingPastEnd_TrimsPartialRecordAndMarksMalformed()
    {
        using var ms = CreateV4WithTelemetryChunkExtendingPastEnd(actualTelemetryPayloadBytes: 10, declaredTelemetryPayloadBytes: 16);

        var result = RawTelemetryData.FromStream(ms);

        Assert.True(result.Malformed);
        Assert.Equal(2, result.Front.Length);
        Assert.Equal(2, result.Rear.Length);
    }

    [Fact]
    public void Parse_PreservesHighAdcSamples_WithoutSignedWrap()
    {
        ushort[] front = [2500, 2510, 2520, 2530, 2540];
        ushort[] rear  = [3000, 3010, 3020, 3030, 3040];

        using var ms = SstTestFiles.CreateV4ParserStream(
            123456789,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Telemetry(front.Zip(rear, (frontSample, rearSample) => (frontSample, rearSample)).ToArray()));
        using var reader = new BinaryReader(ms);

        var result = new SstV4TlvParser().Parse(reader, 4);

        Assert.Equal(front, result.Front);
        Assert.Equal(rear, result.Rear);
    }

    private static MemoryStream CreateV4WithTelemetryChunkExtendingPastEnd(
        int actualTelemetryPayloadBytes,
        int declaredTelemetryPayloadBytes)
    {
        var samplePayload = SstTestFiles.TelemetryPayload((500, 600), (501, 601));
        var payload = new byte[actualTelemetryPayloadBytes];
        Array.Copy(samplePayload, payload, Math.Min(samplePayload.Length, payload.Length));

        return SstTestFiles.CreateV4Stream(
            123456789,
            SstTestFiles.Rates((TlvChunkType.Telemetry, 1000)),
            SstTestFiles.Chunk(TlvChunkType.Telemetry, payload, (ushort)declaredTelemetryPayloadBytes));
    }
}

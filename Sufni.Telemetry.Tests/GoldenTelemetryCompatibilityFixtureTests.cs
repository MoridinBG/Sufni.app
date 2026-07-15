using Sufni.App.ExtensionHost.TestSupport.Fixtures;

namespace Sufni.Telemetry.Tests;

public class GoldenTelemetryCompatibilityFixtureTests
{
    [Theory]
    [InlineData(3, 100, 200)]
    [InlineData(4, 120, 220)]
    [InlineData(5, 140, 240)]
    public void SupportedSstFixture_InspectAndParse_PreservesVersionAndTravel(
        int version,
        ushort expectedFront,
        ushort expectedRear)
    {
        var fixture = SstFixture(version);
        var bytes = fixture.GetBytes();

        var inspection = Assert.IsType<ValidSstFileInspection>(RawTelemetryData.InspectByteArray(bytes));
        var telemetry = RawTelemetryData.FromByteArray(bytes);

        Assert.Equal(version, inspection.Version);
        Assert.Equal(200, inspection.TelemetrySampleRate);
        Assert.Equal(version, telemetry.Version);
        Assert.Equal(expectedFront, telemetry.Front[0]);
        Assert.Equal(expectedRear, telemetry.Rear[0]);
    }

    [Fact]
    public void V4SstFixture_Parse_PreservesDenseImuAndMarker()
    {
        var telemetry = RawTelemetryData.FromByteArray(GoldenTelemetryCompatibilityFixtures.SstV4.GetBytes());

        Assert.Single(telemetry.Markers);
        Assert.NotNull(telemetry.ImuData);
        Assert.Single(telemetry.ImuData.Records);
        var segment = Assert.Single(telemetry.ImuData.Segments);
        Assert.Equal(1, segment.LocationId);
        Assert.Single(segment.Records);
        Assert.Equal(3, telemetry.ImuData.Records[0].Az);
    }

    [Fact]
    public void V5SstFixture_Parse_PreservesSegmentAndCompatibilityImuShapes()
    {
        var telemetry = RawTelemetryData.FromByteArray(GoldenTelemetryCompatibilityFixtures.SstV5.GetBytes());

        Assert.NotNull(telemetry.ImuData);
        Assert.Equal(2, telemetry.ImuData.Records.Count);
        Assert.Equal(2, telemetry.ImuData.Segments.Count);
        Assert.False(telemetry.ImuData.HasGaps);
        Assert.NotNull(telemetry.FinalStatus);
    }

    [Theory]
    [InlineData("dense", 2, 0)]
    [InlineData("segment", 0, 1)]
    [InlineData("dense-plus-segment", 2, 1)]
    public void ProcessedImuFixture_FromBinary_PreservesLegacyRepresentation(
        string shape,
        int expectedDenseCount,
        int expectedSegmentCount)
    {
        var fixture = shape switch
        {
            "dense" => GoldenTelemetryCompatibilityFixtures.ProcessedImuDenseOnly,
            "segment" => GoldenTelemetryCompatibilityFixtures.ProcessedImuSegmentOnly,
            "dense-plus-segment" => GoldenTelemetryCompatibilityFixtures.ProcessedImuDensePlusSegment,
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        var telemetry = TelemetryData.FromBinary(fixture.GetBytes());

        Assert.NotNull(telemetry.ImuData);
        Assert.Equal(expectedDenseCount, telemetry.ImuData.Records.Count);
        Assert.Equal(expectedSegmentCount, telemetry.ImuData.Segments.Count);
        Assert.Equal(100, telemetry.ImuData.SampleRate);
    }

    [Fact]
    public void OldStrokeDigitizationFixture_FromBinary_PreservesAllPersistedIndexes()
    {
        var telemetry = TelemetryData.FromBinary(
            GoldenTelemetryCompatibilityFixtures.ProcessedOldStrokeDigitization.GetBytes());

        var stroke = Assert.Single(telemetry.Front.Strokes.Compressions);
        Assert.Equal([2, 4], stroke.DigitizedTravel);
        Assert.Equal([5, 6], stroke.DigitizedVelocity);
        Assert.Equal([7, 8], stroke.FineDigitizedVelocity);
    }

    [Fact]
    public void SourceLessProcessedFixture_FromBinary_RemainsReadable()
    {
        var telemetry = TelemetryData.FromBinary(
            GoldenTelemetryCompatibilityFixtures.ProcessedSourceLess.GetBytes());

        Assert.Equal("legacy-source-less.sst", telemetry.Metadata.SourceName);
        Assert.Equal(0.02, telemetry.Metadata.Duration);
        Assert.Equal([10, 20], telemetry.Front.Travel);
    }

    private static GoldenTelemetryCompatibilityFixture SstFixture(int version) => version switch
    {
        3 => GoldenTelemetryCompatibilityFixtures.SstV3,
        4 => GoldenTelemetryCompatibilityFixtures.SstV4,
        5 => GoldenTelemetryCompatibilityFixtures.SstV5,
        _ => throw new ArgumentOutOfRangeException(nameof(version)),
    };
}

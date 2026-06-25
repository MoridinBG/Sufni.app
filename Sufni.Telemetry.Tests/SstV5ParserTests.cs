using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class SstV5ParserTests
{
    [Fact]
    public void FromStream_ValidContinuousTravel_RoutesToV5ParserAndCreatesDenseSegments()
    {
        using var stream = SstV5TestFiles.CreateStream(
            1_700_000_000_123,
            Sufni.Telemetry.Tests.SstV5TestFiles.Metadata(Sufni.Telemetry.Tests.SstV5TestFiles.TravelStream()),
            Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                0,
                0,
                Sufni.Telemetry.Tests.SstV5TestFiles.ForkTravel | Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                (1000, 2000),
                (1001, 2001)),
            Sufni.Telemetry.Tests.SstV5TestFiles.FinalStatus(Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus()));

        var result = RawTelemetryData.FromStream(stream);

        Assert.Equal(5, result.Version);
        Assert.Equal(200, result.SampleRate);
        Assert.Equal(1_700_000_000, result.Timestamp);
        Assert.Equal(1_700_000_000_123, result.SessionStartUtcMs);
        Assert.Equal([1000, 1001], result.Front);
        Assert.Equal([2000, 2001], result.Rear);
        var frontSegment = Assert.Single(result.FrontSegments);
        var rearSegment = Assert.Single(result.RearSegments);
        Assert.Equal((ulong)0, frontSegment.FirstIndex);
        Assert.Equal([1000, 1001], frontSegment.Counts);
        Assert.Equal([2000, 2001], rearSegment.Counts);
        Assert.False(result.MissingFinalStatus);
        Assert.NotNull(result.FinalStatus);
        Assert.False(result.Malformed);
    }

    [Fact]
    public void Inspect_MissingFinalStatus_IsImportableWithWarning()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                Sufni.Telemetry.Tests.SstV5TestFiles.Metadata(Sufni.Telemetry.Tests.SstV5TestFiles.TravelStream()),
                Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                    0,
                    0,
                    Sufni.Telemetry.Tests.SstV5TestFiles.ForkTravel | Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                    (1000, 2000))
            ]);

        var inspection = Assert.IsType<ValidSstFileInspection>(RawTelemetryData.InspectStream(stream));

        Assert.Equal(5, inspection.Version);
        Assert.Equal(200, inspection.TelemetrySampleRate);
        Assert.Equal(TimeSpan.FromSeconds(0.005), inspection.Duration);
        Assert.Equal("SST v5 final status is missing; parsed complete data chunks only.", inspection.MalformedMessage);
    }

    [Fact]
    public void Parse_AdjacentTravelChunksWithContiguousTiming_CoalescesSegment()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                Sufni.Telemetry.Tests.SstV5TestFiles.Metadata(Sufni.Telemetry.Tests.SstV5TestFiles.TravelStream()),
                Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                    0,
                    0,
                    Sufni.Telemetry.Tests.SstV5TestFiles.ForkTravel | Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                    (1000, 2000),
                    (1001, 2001)),
                Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                    2,
                    10_000,
                    Sufni.Telemetry.Tests.SstV5TestFiles.ForkTravel | Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                    (1002, 2002)),
                Sufni.Telemetry.Tests.SstV5TestFiles.FinalStatus(Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus())
            ]);

        var result = RawTelemetryData.FromStream(stream);

        Assert.Single(result.FrontSegments);
        Assert.Equal([1000, 1001, 1002], result.FrontSegments[0].Counts);
        Assert.Empty(result.StreamGaps);
    }

    [Fact]
    public void Parse_ContiguousTravelWithMonotonicJitter_CoalescesWithoutSplitting()
    {
        // 200 Hz -> 5000 us/sample. The second batch's monotonic anchor is a few microseconds off
        // the ideal grid (normal monotonic-clock jitter). The sample indices are contiguous, so no
        // samples are missing and the run must stay a single segment with no gaps.
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream()),
                SstV5TestFiles.TravelData(
                    0,
                    0,
                    SstV5TestFiles.ForkTravel | SstV5TestFiles.ShockTravel,
                    (1000, 2000),
                    (1001, 2001)),
                SstV5TestFiles.TravelData(
                    2,
                    10_009, // ideal anchor is 10_000 us; +9 us of jitter must not split the segment
                    SstV5TestFiles.ForkTravel | SstV5TestFiles.ShockTravel,
                    (1002, 2002)),
                SstV5TestFiles.FinalStatus(SstV5TestFiles.OkStatus())
            ]);

        var result = RawTelemetryData.FromStream(stream);

        Assert.Single(result.FrontSegments);
        Assert.Equal([1000, 1001, 1002], result.FrontSegments[0].Counts);
        Assert.Empty(result.StreamGaps);
    }

    [Fact]
    public void Parse_TravelStartingAtNonZeroIndex_DoesNotRecordLeadingGap()
    {
        // The producer trims pre-anchor backlog, so the first emitted sample can carry a non-zero
        // logical index. That is not session loss: the run anchors at this sample's monotonic time
        // without a leading gap, and the file processes like a clean continuous recording.
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream()),
                SstV5TestFiles.TravelData(
                    44,
                    3_574,
                    SstV5TestFiles.ForkTravel | SstV5TestFiles.ShockTravel,
                    (1000, 2000),
                    (1001, 2001)),
                SstV5TestFiles.FinalStatus(SstV5TestFiles.OkStatus())
            ]);

        var result = RawTelemetryData.FromStream(stream);

        var front = Assert.Single(result.FrontSegments);
        Assert.Equal((ulong)44, front.FirstIndex);
        Assert.Empty(result.StreamGaps);
    }

    [Fact]
    public void Parse_TravelIndexGap_CreatesTwoSegmentsAndGap()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                Sufni.Telemetry.Tests.SstV5TestFiles.Metadata(Sufni.Telemetry.Tests.SstV5TestFiles.TravelStream()),
                Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                    0,
                    0,
                    Sufni.Telemetry.Tests.SstV5TestFiles.ForkTravel | Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                    2,
                    10_000,
                    Sufni.Telemetry.Tests.SstV5TestFiles.ForkTravel | Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                    (1002, 2002)),
                Sufni.Telemetry.Tests.SstV5TestFiles.FinalStatus(Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus())
            ]);

        var result = RawTelemetryData.FromStream(stream);

        Assert.Equal(2, result.FrontSegments.Length);
        Assert.Equal([1000], result.FrontSegments[0].Counts);
        Assert.Equal([1002], result.FrontSegments[1].Counts);
        Assert.Contains(result.StreamGaps, gap =>
            gap.StreamKind == Sufni.Telemetry.Tests.SstV5TestFiles.StreamTravel &&
            gap.FirstMissingIndex == 1 &&
            gap.MissingCount == 1 &&
            gap.Reason == "index_gap");
    }

    [Fact]
    public void Parse_InvalidFrontValidityWithRearValid_CreatesFrontGapOnly()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                Sufni.Telemetry.Tests.SstV5TestFiles.Metadata(Sufni.Telemetry.Tests.SstV5TestFiles.TravelStream()),
                Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                    0,
                    0,
                    Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                Sufni.Telemetry.Tests.SstV5TestFiles.FinalStatus(Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus())
            ]);

        var result = RawTelemetryData.FromStream(stream);

        Assert.Empty(result.FrontSegments);
        Assert.Single(result.RearSegments);
        var gap = Assert.Single(result.StreamGaps);
        Assert.Equal("invalid_validity", gap.Reason);
        Assert.Equal((ulong)0, gap.FirstMissingIndex);
        Assert.Equal((ulong)1, gap.MissingCount);
        // The gap is attributed to the fork (front) sensor bit so the processing layer can
        // flag only the front side, not the rear.
        Assert.Equal((byte?)SstV5TestFiles.ForkTravel, gap.LocationId);
    }

    [Fact]
    public void Inspect_MissingTravelDescriptor_ReturnsMalformedInspection()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                Sufni.Telemetry.Tests.SstV5TestFiles.Metadata(Sufni.Telemetry.Tests.SstV5TestFiles.MarkerStream()),
                Sufni.Telemetry.Tests.SstV5TestFiles.MarkerData(0, 0, 1),
                Sufni.Telemetry.Tests.SstV5TestFiles.FinalStatus(Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus())
            ]);

        var inspection = Assert.IsType<MalformedSstFileInspection>(RawTelemetryData.InspectStream(stream));

        Assert.Equal("SST v5 travel data is missing or unsupported by this app.", inspection.Message);
    }

    [Fact]
    public void Inspect_NonIntegerTravelRate_ReturnsMalformedInspection()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                Sufni.Telemetry.Tests.SstV5TestFiles.Metadata(Sufni.Telemetry.Tests.SstV5TestFiles.TravelStream(rateMhz: 200_500)),
                Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                    0,
                    0,
                    Sufni.Telemetry.Tests.SstV5TestFiles.ForkTravel | Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                Sufni.Telemetry.Tests.SstV5TestFiles.FinalStatus(Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus())
            ]);

        var inspection = Assert.IsType<MalformedSstFileInspection>(RawTelemetryData.InspectStream(stream));

        Assert.Equal("SST v5 travel data is missing or unsupported by this app.", inspection.Message);
    }

    [Fact]
    public void Parse_DenseImu_CreatesMetaSegmentsActiveLocationsAndCompatibilityRecords()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                Sufni.Telemetry.Tests.SstV5TestFiles.Metadata(
                    Sufni.Telemetry.Tests.SstV5TestFiles.TravelStream(),
                    Sufni.Telemetry.Tests.SstV5TestFiles.ImuStream()),
                Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                    0,
                    0,
                    Sufni.Telemetry.Tests.SstV5TestFiles.ForkTravel | Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                Sufni.Telemetry.Tests.SstV5TestFiles.ImuData(
                    0,
                    0,
                    Sufni.Telemetry.Tests.SstV5TestFiles.FrameImu | Sufni.Telemetry.Tests.SstV5TestFiles.ForkImu,
                    (new ImuRecordSpec(1, 2, 3, 4, 5, 6), new ImuRecordSpec(7, 8, 9, 10, 11, 12))),
                Sufni.Telemetry.Tests.SstV5TestFiles.FinalStatus(
                    Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus(),
                    Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus())
            ]);

        var result = RawTelemetryData.FromStream(stream);

        Assert.NotNull(result.ImuData);
        Assert.Equal([0, 1], result.ImuData.ActiveLocations);
        Assert.Equal(2, result.ImuData.Meta.Count);
        Assert.Equal(2, result.ImuData.Segments.Count);
        Assert.Equal(2, result.ImuData.Records.Count);
        Assert.False(result.ImuData.HasGaps);
        Assert.Equal(1, result.ImuData.Records[0].Ax);
        Assert.Equal(7, result.ImuData.Records[1].Ax);
    }

    [Fact]
    public void Parse_GpsM8NWithDiagnostics_DecodesScaledFields()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                Sufni.Telemetry.Tests.SstV5TestFiles.Metadata(
                    Sufni.Telemetry.Tests.SstV5TestFiles.TravelStream(),
                    Sufni.Telemetry.Tests.SstV5TestFiles.GpsStream(diagnostics: true)),
                Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                    0,
                    0,
                    Sufni.Telemetry.Tests.SstV5TestFiles.ForkTravel | Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                Sufni.Telemetry.Tests.SstV5TestFiles.GpsM8NData(
                    0,
                    0,
                    diagnostics: true,
                    new V5GpsM8NRecord(20250106, 43_200_000, 471234560, 196543210)),
                Sufni.Telemetry.Tests.SstV5TestFiles.FinalStatus(
                    Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus(),
                    Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus())
            ]);

        var result = RawTelemetryData.FromStream(stream);

        var gps = Assert.Single(result.GpsData!);
        Assert.Equal(new DateTime(2025, 1, 6, 12, 0, 0, DateTimeKind.Utc), gps.Timestamp);
        Assert.Equal(47.123456, gps.Latitude, 6);
        Assert.Equal(19.654321, gps.Longitude, 6);
        Assert.Equal(123.0f, gps.Altitude);
        Assert.Equal(12.5f, gps.Speed);
        Assert.Equal(90.0f, gps.Heading);
        Assert.Equal(2.5f, gps.Epe2d);
        Assert.Equal(3.5f, gps.Epe3d);
    }

    [Fact]
    public void Parse_TemperatureMarkerBatteryAndFinalStatus_DecodesSupportedAppData()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                Sufni.Telemetry.Tests.SstV5TestFiles.Metadata(
                    Sufni.Telemetry.Tests.SstV5TestFiles.TravelStream(),
                    Sufni.Telemetry.Tests.SstV5TestFiles.TemperatureStream(),
                    Sufni.Telemetry.Tests.SstV5TestFiles.BatteryStream(),
                    Sufni.Telemetry.Tests.SstV5TestFiles.MarkerStream()),
                Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                    0,
                    0,
                    Sufni.Telemetry.Tests.SstV5TestFiles.ForkTravel | Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                Sufni.Telemetry.Tests.SstV5TestFiles.TemperatureData(0, 2_000_000, Sufni.Telemetry.Tests.SstV5TestFiles.FrameImu, 340),
                Sufni.Telemetry.Tests.SstV5TestFiles.BatteryData(0, 3_000_000, (4200, 0x0003)),
                Sufni.Telemetry.Tests.SstV5TestFiles.MarkerData(0, 4_000_000, 1),
                Sufni.Telemetry.Tests.SstV5TestFiles.FinalStatus(
                    Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus(),
                    Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus(),
                    Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus(),
                    Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus())
            ]);

        var result = RawTelemetryData.FromStream(stream);

        var temperature = Assert.Single(result.TemperatureData);
        Assert.Equal((byte)0, temperature.LocationId);
        Assert.Equal(37.53f, temperature.TemperatureCelsius, 2);
        var marker = Assert.Single(result.Markers);
        Assert.Equal(4.0, marker.TimestampOffset);
        Assert.NotNull(result.FinalStatus);
        Assert.Equal(4, result.FinalStatus.Streams.Length);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)3)]
    public void Inspect_InvalidFinalStatusSessionResult_ReturnsMalformed(byte sessionResultReason)
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                Sufni.Telemetry.Tests.SstV5TestFiles.Metadata(Sufni.Telemetry.Tests.SstV5TestFiles.TravelStream()),
                Sufni.Telemetry.Tests.SstV5TestFiles.TravelData(
                    0,
                    0,
                    Sufni.Telemetry.Tests.SstV5TestFiles.ForkTravel | Sufni.Telemetry.Tests.SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                Sufni.Telemetry.Tests.SstV5TestFiles.FinalStatus(sessionResultReason, Sufni.Telemetry.Tests.SstV5TestFiles.OkStatus())
            ]);

        var inspection = Assert.IsType<MalformedSstFileInspection>(RawTelemetryData.InspectStream(stream));

        Assert.NotEmpty(inspection.Message);
    }

    [Fact]
    public void Parse_GpsRecordWithUnknownFixMode_IsSkippedWhileFileImports()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                SstV5TestFiles.Metadata(
                    SstV5TestFiles.TravelStream(),
                    SstV5TestFiles.GpsStream()),
                SstV5TestFiles.TravelData(
                    0,
                    0,
                    SstV5TestFiles.ForkTravel | SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                SstV5TestFiles.GpsM8NData(
                    0,
                    0,
                    diagnostics: false,
                    new V5GpsM8NRecord(20250106, 43_200_000, 471234560, 196543210, FixMode: 1),
                    new V5GpsM8NRecord(20250106, 43_300_000, 471234560, 196543210, FixMode: 3)),
                SstV5TestFiles.FinalStatus(SstV5TestFiles.OkStatus(), SstV5TestFiles.OkStatus())
            ]);

        var result = RawTelemetryData.FromStream(stream);

        // The unpublishable fix-mode record is dropped, the valid one is kept, and the
        // travel-bearing file still imports.
        Assert.Single(result.FrontSegments);
        var gps = Assert.Single(result.GpsData!);
        Assert.Equal((byte)3, gps.FixMode);
    }

    [Fact]
    public void Parse_BatteryRecordWithUndefinedFlagBits_StillImports()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream(), SstV5TestFiles.BatteryStream()),
                SstV5TestFiles.TravelData(
                    0,
                    0,
                    SstV5TestFiles.ForkTravel | SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                SstV5TestFiles.BatteryData(0, 1_000_000, (4200, 0xFFFF)),
                SstV5TestFiles.FinalStatus(SstV5TestFiles.OkStatus(), SstV5TestFiles.OkStatus())
            ]);

        var result = RawTelemetryData.FromStream(stream);

        Assert.Single(result.FrontSegments);
        Assert.Single(result.RearSegments);
    }

    [Fact]
    public void Parse_FinalStatusWithUndefinedProducerState_ImportsAndStoresRawState()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream()),
                SstV5TestFiles.TravelData(
                    0,
                    0,
                    SstV5TestFiles.ForkTravel | SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                SstV5TestFiles.FinalStatus(new V5FinalStreamStatus(3, 0, 0, 0, 0, 0))
            ]);

        var result = RawTelemetryData.FromStream(stream);

        Assert.NotNull(result.FinalStatus);
        var streamStatus = Assert.Single(result.FinalStatus.Streams);
        Assert.Equal((byte)3, streamStatus.ProducerState);
    }
}

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
    }

    [Fact]
    public void FromMemory_ArraySlice_ParsesOnlyTheLogicalV5Bytes()
    {
        using var stream = SstV5TestFiles.CreateStream(
            1_700_000_000_123,
            SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream()),
            SstV5TestFiles.TravelData(
                0,
                0,
                SstV5TestFiles.ForkTravel | SstV5TestFiles.ShockTravel,
                (1000, 2000),
                (1001, 2001)),
            SstV5TestFiles.FinalStatus(SstV5TestFiles.OkStatus()));
        var bytes = stream.ToArray();
        var backing = new byte[bytes.Length + 17];
        bytes.CopyTo(backing, 7);

        var result = RawTelemetryData.FromMemory(backing.AsMemory(7, bytes.Length));

        Assert.Equal(5, result.Version);
        Assert.Equal([1000, 1001], result.Front);
        Assert.Equal([2000, 2001], result.Rear);
        Assert.False(result.MissingFinalStatus);
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
        Assert.False(string.IsNullOrWhiteSpace(inspection.MalformedMessage));
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

        Assert.Equal((byte)5, inspection.Version);
        Assert.Null(inspection.TelemetrySampleRate);
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

        Assert.Equal((byte)5, inspection.Version);
        Assert.Null(inspection.TelemetrySampleRate);
    }

    [Fact]
    public void Parse_DenseImu_CreatesCanonicalSegmentsWithoutDenseDuplicate()
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
        Assert.Equal(100, result.ImuData.SampleRate);
        var segments = result.ImuData.SampleSegments;
        Assert.Equal(2, segments.Count);
        Assert.Equal((byte)0, segments[0].LocationId);
        Assert.Equal((byte)1, segments[1].LocationId);
        Assert.Equal(new ImuRecord(1, 2, 3, 4, 5, 6), segments[0][0]);
        Assert.Equal(new ImuRecord(7, 8, 9, 10, 11, 12), segments[1][0]);
        Assert.All(new[] { segments[0], segments[1] }, segment =>
        {
            Assert.Equal((ulong)0, segment.FirstMonotonicDeltaUs);
            Assert.Equal(1, segment.Count);
        });
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
    public void Parse_TemperatureData_ConvertsCountsAndSourceTime()
    {
        using var stream = SstV5TestFiles.CreateStream(chunks:
        [
            SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream(), SstV5TestFiles.TemperatureStream()),
            SstV5TestFiles.TravelData(0, 0, SstV5TestFiles.ForkTravel | SstV5TestFiles.ShockTravel, (1000, 2000)),
            SstV5TestFiles.TemperatureData(0, 2_000_000, SstV5TestFiles.FrameImu, 340),
        ]);

        var temperature = Assert.Single(RawTelemetryData.FromStream(stream).TemperatureData);

        Assert.Equal((byte)0, temperature.LocationId);
        Assert.Equal(1_700_000_002, temperature.TimestampUtc);
        Assert.Equal(37.53f, temperature.TemperatureCelsius, 2);
    }

    [Fact]
    public void Parse_MarkerData_UsesMonotonicSourceTime()
    {
        using var stream = SstV5TestFiles.CreateStream(chunks:
        [
            SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream(), SstV5TestFiles.MarkerStream()),
            SstV5TestFiles.TravelData(0, 0, SstV5TestFiles.ForkTravel | SstV5TestFiles.ShockTravel, (1000, 2000)),
            SstV5TestFiles.MarkerData(0, 4_000_000, 1),
        ]);

        Assert.Equal(4.0, Assert.Single(RawTelemetryData.FromStream(stream).Markers).TimestampOffset);
    }

    [Fact]
    public void Inspect_MarkerDataWithMultipleSamples_ReturnsMalformed()
    {
        using var stream = SstV5TestFiles.CreateStream(
            chunks:
            [
                SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream(), SstV5TestFiles.MarkerStream()),
                SstV5TestFiles.TravelData(
                    0,
                    0,
                    SstV5TestFiles.ForkTravel | SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                SstV5TestFiles.MarkerData(0, 1_000_000, 1, 1),
                SstV5TestFiles.FinalStatus(SstV5TestFiles.OkStatus(), SstV5TestFiles.OkStatus())
            ]);

        var inspection = Assert.IsType<MalformedSstFileInspection>(RawTelemetryData.InspectStream(stream));

        Assert.Equal((ushort)200, inspection.TelemetrySampleRate);
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

    [Theory]
    [InlineData(0)] // File flags.
    [InlineData(1)] // Data preceding metadata.
    [InlineData(2)] // Chunk flags.
    [InlineData(3)] // Data following final status.
    public void Inspect_MalformedEnvelope_RejectsInvalidFraming(int cause)
    {
        var bytes = cause switch
        {
            0 => SstV5TestFiles.Create(chunks: [SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream())]),
            1 => SstV5TestFiles.Create(chunks: [SstV5TestFiles.TravelData(0, 0, SstV5TestFiles.ForkTravel, (1000, 0))]),
            2 => SstV5TestFiles.Create(chunks: [SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream()), SstV5TestFiles.Chunk(999, [], flags: 1)]),
            _ => SstV5TestFiles.Create(chunks: [SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream()), SstV5TestFiles.FinalStatus(SstV5TestFiles.OkStatus()), SstV5TestFiles.TravelData(0, 0, SstV5TestFiles.ForkTravel, (1000, 0))]),
        };
        if (cause == 0) bytes[6] = 1;

        Assert.IsType<MalformedSstFileInspection>(RawTelemetryData.InspectByteArray(bytes));
        Assert.Throws<FormatException>(() => RawTelemetryData.FromByteArray(bytes));
    }

    [Fact]
    public void Inspect_TruncatedPayloadAfterCompleteTravel_PreservesKnownPrefix()
    {
        var bytes = SstV5TestFiles.Create(
            chunks:
            [
                SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream()),
                SstV5TestFiles.TravelData(
                    0,
                    0,
                    SstV5TestFiles.ForkTravel | SstV5TestFiles.ShockTravel,
                    (1000, 2000)),
                SstV5TestFiles.Chunk(SstV5TestFiles.ChunkImuData, [1], declaredLength: 128)
            ]);

        var inspection = Assert.IsType<ValidSstFileInspection>(RawTelemetryData.InspectByteArray(bytes));
        var parsed = RawTelemetryData.FromByteArray(bytes);

        Assert.False(string.IsNullOrWhiteSpace(inspection.MalformedMessage));
        Assert.Equal(TimeSpan.FromMilliseconds(5), inspection.Duration);
        Assert.Equal(200, inspection.TelemetrySampleRate);
        Assert.Equal(new DateTime(2023, 11, 14, 22, 13, 20, 123, DateTimeKind.Utc), inspection.StartTime);
        Assert.Equal([1000], parsed.Front);
        Assert.Equal([2000], parsed.Rear);
    }

    [Fact]
    public void Inspect_TruncatedPayloadAfterOnlyInvalidTravel_RemainsUnsupported()
    {
        var bytes = SstV5TestFiles.Create(
            chunks:
            [
                SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream()),
                SstV5TestFiles.TravelData(0, 0, 0, (1000, 2000)),
                SstV5TestFiles.Chunk(SstV5TestFiles.ChunkImuData, [1], declaredLength: 128)
            ]);

        Assert.IsType<MalformedSstFileInspection>(RawTelemetryData.InspectByteArray(bytes));
        Assert.Throws<FormatException>(() => RawTelemetryData.FromByteArray(bytes));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Inspect_IncompletePayloadOrHeader_RejectsUnsuitablePrefix(bool incompleteHeader)
    {
        var prefix = SstV5TestFiles.Create(chunks:
        [
            SstV5TestFiles.Metadata(SstV5TestFiles.TravelStream()),
            incompleteHeader
                ? SstV5TestFiles.TravelData(0, 0, SstV5TestFiles.ForkTravel, (1000, 0))
                : SstV5TestFiles.Chunk(SstV5TestFiles.ChunkTravelData, [1], declaredLength: 128),
        ]);
        byte[] bytes = incompleteHeader ? [.. prefix, 1, 2, 3, 4] : prefix;

        Assert.IsType<MalformedSstFileInspection>(RawTelemetryData.InspectByteArray(bytes));
        Assert.Throws<FormatException>(() => RawTelemetryData.FromByteArray(bytes));
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

    [Fact]
    public void Parse_FinalStatusWithSinkBacklog_ImportsAndStoresBacklog()
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
                SstV5TestFiles.FinalStatus(new V5FinalStreamStatus(1, 0, 0, 0, 0, 0, SinkBacklogBatches: 12))
            ]);

        var result = RawTelemetryData.FromStream(stream);

        Assert.NotNull(result.FinalStatus);
        var streamStatus = Assert.Single(result.FinalStatus.Streams);
        Assert.Equal((ushort)12, streamStatus.SinkBacklogBatches);
    }

}

using System.Text;
using MessagePack;
using Sufni.App.ExtensionHost.TestSupport.Fixtures;

namespace Sufni.Telemetry.Tests;

public class GoldenTelemetryCompatibilityFixtureTests
{
    private const string FrozenLegacyImuBase64 =
        "hqRNZXRhkoOqTG9jYXRpb25JZAGsQWNjZWxMc2JQZXJHykaAAACtR3lyb0xzYlBlckRwc8pDAzMzg6pMb2NhdGlvbklkAqxBY2NlbExzYlBlckfKRgAAAK1HeXJvTHNiUGVyRHBzykKDMzOqU2FtcGxlUmF0ZczIp1JlY29yZHOQr0FjdGl2ZUxvY2F0aW9uc5IBAqhTZWdtZW50c5KEqkxvY2F0aW9uSWQBqkZpcnN0SW5kZXgKtUZpcnN0TW9ub3RvbmljRGVsdGFVc84AAYagp1JlY29yZHOShqJBeNGAAKJBef+iQXoAokd4AaJHec1//6JHeiqGokF4B6JBeQiiQXoJokd4CqJHeQuiR3oMhKpMb2NhdGlvbklkAqpGaXJzdEluZGV4KLVGaXJzdE1vbm90b25pY0RlbHRhVXPOAAYagKdSZWNvcmRzkYaiQXhkokF5zMiiQXrNASyiR3jQnKJHedH/OKJHetH+1KdIYXNHYXBzww==";
    private const string FrozenCompactImuBase64 =
        "lgHMyJKTAcpGgAAAykMDMzOTAspGAAAAykKDMzPEAgECkpQBCs4AAYagkpbRgAD/AAHNf/8qlgcICQoLDJQCKM4ABhqAkZZkzMjNASzQnNH/ONH+1MM=";

    [Fact]
    public void CurrentImuWriter_UsesFrozenCompactVersionedShape()
    {
        var imu = CreatePerf04GoldenImu();

        var bytes = MessagePackSerializer.Serialize(
            imu,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(FrozenCompactImuBase64, Convert.ToBase64String(bytes));
    }

    [Theory]
    [InlineData("dense", 1, 2)]
    [InlineData("segment", 1, 2)]
    [InlineData("dense-plus-segment", 1, 2)]
    public void LegacyProcessedImu_ReadApi_PreservesOneLogicalSampleSequence(
        string shape,
        int expectedSegmentCount,
        int expectedSampleCount)
    {
        var fixture = ProcessedImuFixture(shape);

        var telemetry = TelemetryData.FromBinary(fixture.GetBytes());

        Assert.NotNull(telemetry.ImuData);
        Assert.Equal(expectedSegmentCount, telemetry.ImuData.SampleSegments.Count);
        var segment = telemetry.ImuData.SampleSegments[0];
        Assert.Equal(1, segment.LocationId);
        Assert.Equal(expectedSampleCount, segment.Count);
        Assert.Equal([1, 7], ReadSamples(segment).Select(sample => (int)sample.Ax));
    }

    [Fact]
    public void LegacyDenseOnlyImu_RewritePreservesEffectiveActiveLocations()
    {
        var legacy = TelemetryData.FromBinary(
            GoldenTelemetryCompatibilityFixtures.ProcessedImuDenseOnly.GetBytes());

        var rewritten = TelemetryData.FromBinary(legacy.BinaryForm);

        Assert.NotNull(rewritten.ImuData);
        Assert.Equal([1], rewritten.ImuData.ActiveLocations);
        Assert.Empty(rewritten.ImuData.Records);
        var segment = Assert.Single(rewritten.ImuData.Segments);
        Assert.Equal(1, segment.LocationId);
        Assert.Equal([1, 7], segment.Records.Select(sample => (int)sample.Ax));
    }

    [Fact]
    public void LegacyInterleavedDenseImu_ReadApi_ExposesStridedLocationSegments()
    {
        var imu = new RawImuData
        {
            SampleRate = 100,
            ActiveLocations = [1, 2],
            Records =
            [
                new ImuRecord(10, 0, 0, 0, 0, 0),
                new ImuRecord(20, 0, 0, 0, 0, 0),
                new ImuRecord(11, 0, 0, 0, 0, 0),
                new ImuRecord(21, 0, 0, 0, 0, 0),
            ],
        };

        Assert.Equal(2, imu.SampleSegments.Count);
        Assert.Equal(1, imu.SampleSegments[0].LocationId);
        Assert.Equal([10, 11], ReadSamples(imu.SampleSegments[0]).Select(sample => (int)sample.Ax));
        Assert.False(imu.SampleSegments[0].TryGetContiguousRecords(out _));
        Assert.Equal(2, imu.SampleSegments[1].LocationId);
        Assert.Equal([20, 21], ReadSamples(imu.SampleSegments[1]).Select(sample => (int)sample.Ax));
        Assert.False(imu.SampleSegments[1].TryGetContiguousRecords(out _));
    }

    [Fact]
    public void CurrentImuWriter_ConvertsLegacyDenseOnlyDataToCompactSegments()
    {
        var imu = new RawImuData
        {
            SampleRate = 100,
            ActiveLocations = [1, 2],
            Records =
            [
                new ImuRecord(10, 0, 0, 0, 0, 0),
                new ImuRecord(20, 0, 0, 0, 0, 0),
                new ImuRecord(11, 0, 0, 0, 0, 0),
                new ImuRecord(21, 0, 0, 0, 0, 0),
            ],
        };

        var bytes = MessagePackSerializer.Serialize(
            imu,
            cancellationToken: TestContext.Current.CancellationToken);
        var decoded = MessagePackSerializer.Deserialize<RawImuData>(
            bytes,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(decoded.Records);
        Assert.Equal(2, decoded.Segments.Count);
        Assert.Equal([10, 11], decoded.Segments[0].Records.Select(sample => (int)sample.Ax));
        Assert.Equal([20, 21], decoded.Segments[1].Records.Select(sample => (int)sample.Ax));
    }

    [Fact]
    public void CurrentImuWriter_PrefersCanonicalSegmentsOverDenseCompatibilityData()
    {
        var imu = CreatePerf04GoldenImu();
        imu.Records = [new ImuRecord(999, 0, 0, 0, 0, 0)];

        var bytes = MessagePackSerializer.Serialize(
            imu,
            cancellationToken: TestContext.Current.CancellationToken);
        var decoded = MessagePackSerializer.Deserialize<RawImuData>(
            bytes,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(decoded.Records);
        Assert.Equal(2, decoded.Segments.Count);
        Assert.Equal(short.MinValue, decoded.Segments[0].Records[0].Ax);
    }

    [Fact]
    public void CompactImuFixture_ReadsSelectedVersionedValueSegments()
    {
        var bytes = Convert.FromBase64String(FrozenCompactImuBase64);

        var imu = MessagePackSerializer.Deserialize<RawImuData>(
            bytes,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(200, imu.SampleRate);
        Assert.Equal([1, 2], imu.ActiveLocations);
        Assert.Empty(imu.Records);
        Assert.Equal(2, imu.Segments.Count);
        Assert.Equal(2, imu.SampleSegments.Count);

        var first = imu.SampleSegments[0];
        Assert.Equal(1, first.LocationId);
        Assert.Equal((ulong)10, first.FirstIndex);
        Assert.Equal((ulong)100_000, first.FirstMonotonicDeltaUs);
        Assert.True(first.TryGetContiguousRecords(out var contiguous));
        Assert.Equal(2, contiguous.Length);
        Assert.Equal(new ImuRecord(short.MinValue, -1, 0, 1, short.MaxValue, 42), first[0]);
        Assert.Equal(new ImuRecord(7, 8, 9, 10, 11, 12), first[1]);

        var second = imu.SampleSegments[1];
        Assert.Equal(2, second.LocationId);
        Assert.Equal((ulong)40, second.FirstIndex);
        Assert.Equal((ulong)400_000, second.FirstMonotonicDeltaUs);
        Assert.Equal(new ImuRecord(100, 200, 300, -100, -200, -300), second[0]);
    }

    [Fact]
    public void CompactImuFixture_IntegratesWithProcessedTelemetryReader()
    {
        var compact = CreateCompactTelemetry();
        var bytes = MessagePackSerializer.Serialize(
            compact,
            cancellationToken: TestContext.Current.CancellationToken);

        var telemetry = TelemetryData.FromBinary(bytes);

        Assert.NotNull(telemetry.ImuData);
        Assert.Equal(2, telemetry.ImuData.SampleSegments.Count);
        Assert.Equal(new ImuRecord(100, 200, 300, -100, -200, -300), telemetry.ImuData.SampleSegments[1][0]);
    }

    [Fact]
    public void CompactImuFixture_RejectsUnsupportedEncodingVersion()
    {
        var bytes = Convert.FromBase64String(FrozenCompactImuBase64);
        bytes[1] = 2;

        Assert.Throws<MessagePackSerializationException>(() =>
            MessagePackSerializer.Deserialize<RawImuData>(
                bytes,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void LegacyAndCompactImuFixtures_RejectTruncatedPayloads()
    {
        var legacy = Convert.FromBase64String(FrozenLegacyImuBase64);
        var compact = Convert.FromBase64String(FrozenCompactImuBase64);

        Assert.Throws<MessagePackSerializationException>(() =>
            MessagePackSerializer.Deserialize<RawImuData>(
                legacy[..^1],
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Throws<MessagePackSerializationException>(() =>
            MessagePackSerializer.Deserialize<RawImuData>(
                compact[..^1],
                cancellationToken: TestContext.Current.CancellationToken));
    }

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
    public void V4SstFixture_Parse_UsesCanonicalImuSegmentAndPreservesMarker()
    {
        var telemetry = RawTelemetryData.FromByteArray(GoldenTelemetryCompatibilityFixtures.SstV4.GetBytes());

        Assert.Single(telemetry.Markers);
        Assert.NotNull(telemetry.ImuData);
        Assert.Empty(telemetry.ImuData.Records);
        var segment = Assert.Single(telemetry.ImuData.Segments);
        Assert.Equal(1, segment.LocationId);
        Assert.Single(segment.Records);
        Assert.Equal(3, segment.Records[0].Az);
    }

    [Fact]
    public void V5SstFixture_Parse_PreservesCanonicalImuSegments()
    {
        var telemetry = RawTelemetryData.FromByteArray(GoldenTelemetryCompatibilityFixtures.SstV5.GetBytes());

        Assert.NotNull(telemetry.ImuData);
        Assert.Empty(telemetry.ImuData.Records);
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
        var fixture = ProcessedImuFixture(shape);

        var telemetry = TelemetryData.FromBinary(fixture.GetBytes());

        Assert.NotNull(telemetry.ImuData);
        Assert.Equal(expectedDenseCount, telemetry.ImuData.Records.Count);
        Assert.Equal(expectedSegmentCount, telemetry.ImuData.Segments.Count);
        Assert.Equal(100, telemetry.ImuData.SampleRate);
    }

    [Fact]
    public void CurrentStrokeWriter_OmitsAllDigitizationFields()
    {
        var stroke = new Stroke
        {
            Start = 1,
            End = 2,
            Stat = new StrokeStat { Count = 2 },
            DigitizedTravel = [3, 4],
            DigitizedVelocity = [5, 6],
            FineDigitizedVelocity = [7, 8],
            StartSeconds = 0.1,
            EndSeconds = 0.2,
        };

        var bytes = MessagePackSerializer.Serialize(
            stroke,
            cancellationToken: TestContext.Current.CancellationToken);
        var reader = new MessagePackReader(bytes);
        var fieldCount = reader.ReadMapHeader();
        var fields = new List<string>(fieldCount);
        for (var fieldIndex = 0; fieldIndex < fieldCount; fieldIndex++)
        {
            fields.Add(reader.ReadString()!);
            reader.Skip();
        }
        var rewritten = MessagePackSerializer.Deserialize<Stroke>(
            bytes,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                nameof(Stroke.Start),
                nameof(Stroke.End),
                nameof(Stroke.Stat),
                nameof(Stroke.StartSeconds),
                nameof(Stroke.EndSeconds),
            ],
            fields);
        Assert.Empty(rewritten.DigitizedTravel);
        Assert.Empty(rewritten.DigitizedVelocity);
        Assert.Empty(rewritten.FineDigitizedVelocity);
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
    public void OldStrokeDigitizationFixture_RewriteOmitsAllDigitizationFields()
    {
        var telemetry = TelemetryData.FromBinary(
            GoldenTelemetryCompatibilityFixtures.ProcessedOldStrokeDigitization.GetBytes());

        var rewrittenBytes = telemetry.BinaryForm;
        var rewritten = TelemetryData.FromBinary(rewrittenBytes);

        Assert.Equal(
            -1,
            rewrittenBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(nameof(Stroke.DigitizedTravel))));
        Assert.Equal(
            -1,
            rewrittenBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(nameof(Stroke.DigitizedVelocity))));
        Assert.Equal(
            -1,
            rewrittenBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(nameof(Stroke.FineDigitizedVelocity))));
        var stroke = Assert.Single(rewritten.Front.Strokes.Compressions);
        Assert.Empty(stroke.DigitizedTravel);
        Assert.Empty(stroke.DigitizedVelocity);
        Assert.Empty(stroke.FineDigitizedVelocity);
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

    private static GoldenTelemetryCompatibilityFixture ProcessedImuFixture(string shape) => shape switch
    {
        "dense" => GoldenTelemetryCompatibilityFixtures.ProcessedImuDenseOnly,
        "segment" => GoldenTelemetryCompatibilityFixtures.ProcessedImuSegmentOnly,
        "dense-plus-segment" => GoldenTelemetryCompatibilityFixtures.ProcessedImuDensePlusSegment,
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    private static ImuRecord[] ReadSamples(ImuSampleSegment segment)
    {
        var samples = new ImuRecord[segment.Count];
        var index = 0;
        foreach (var sample in segment)
        {
            samples[index++] = sample;
        }
        return samples;
    }

    private static RawImuData CreatePerf04GoldenImu() => new()
    {
        SampleRate = 200,
        Meta =
        [
            new ImuMetaEntry(1, 16384.0f, 131.2f),
            new ImuMetaEntry(2, 8192.0f, 65.6f),
        ],
        ActiveLocations = [1, 2],
        HasGaps = true,
        Segments =
        [
            new RawImuSegment
            {
                LocationId = 1,
                FirstIndex = 10,
                FirstMonotonicDeltaUs = 100_000,
                Records =
                [
                    new ImuRecord(short.MinValue, -1, 0, 1, short.MaxValue, 42),
                    new ImuRecord(7, 8, 9, 10, 11, 12),
                ],
            },
            new RawImuSegment
            {
                LocationId = 2,
                FirstIndex = 40,
                FirstMonotonicDeltaUs = 400_000,
                Records = [new ImuRecord(100, 200, 300, -100, -200, -300)],
            },
        ],
    };

    private static CompactTelemetryData CreateCompactTelemetry() => new()
    {
        Metadata = new Metadata { SourceName = "compact.sst", Version = 5, SampleRate = 100, Duration = 0.02 },
        Front = EmptySuspension(),
        Rear = EmptySuspension(),
        Airtimes = [],
        ImuData = CompactImuData.From(CreatePerf04GoldenImu()),
    };

    private static Suspension EmptySuspension() => new()
    {
        Travel = [],
        Velocity = [],
        Strokes = new Strokes(),
        TravelBins = [],
        VelocityBins = [],
        FineVelocityBins = [],
    };

    [MessagePackObject(keyAsPropertyName: true, AllowPrivate = true)]
    internal sealed class CompactTelemetryData
    {
        public Metadata Metadata { get; set; } = new();
        public Suspension Front { get; set; } = EmptySuspension();
        public Suspension Rear { get; set; } = EmptySuspension();
        public Airtime[] Airtimes { get; set; } = [];
        public MarkerData[] Markers { get; set; } = [];
        public CompactImuData? ImuData { get; set; }
        public GpsRecord[]? GpsData { get; set; }
        public TemperatureAverage[] TemperatureAverages { get; set; } = [];
        public RawStreamGap[] StreamGaps { get; set; } = [];
        public SstFinalStatus? FinalStatus { get; set; }
        public bool MissingFinalStatus { get; set; }
    }

    [MessagePackObject(AllowPrivate = true)]
    internal sealed class CompactImuData
    {
        [Key(0)] public int EncodingVersion { get; set; } = 1;
        [Key(1)] public int SampleRate { get; set; }
        [Key(2)] public CompactImuMeta[] Meta { get; set; } = [];
        [Key(3)] public byte[] ActiveLocations { get; set; } = [];
        [Key(4)] public CompactImuSegment[] Segments { get; set; } = [];
        [Key(5)] public bool HasGaps { get; set; }

        public static CompactImuData From(RawImuData source) => new()
        {
            SampleRate = source.SampleRate,
            Meta = source.Meta.Select(value => new CompactImuMeta(
                value.LocationId,
                value.AccelLsbPerG,
                value.GyroLsbPerDps)).ToArray(),
            ActiveLocations = [.. source.ActiveLocations],
            Segments = source.Segments.Select(CompactImuSegment.From).ToArray(),
            HasGaps = source.HasGaps,
        };
    }

    [MessagePackObject(AllowPrivate = true)]
    internal readonly record struct CompactImuMeta(
        [property: Key(0)] byte LocationId,
        [property: Key(1)] float AccelLsbPerG,
        [property: Key(2)] float GyroLsbPerDps);

    [MessagePackObject(AllowPrivate = true)]
    internal sealed class CompactImuSegment
    {
        [Key(0)] public byte LocationId { get; set; }
        [Key(1)] public ulong FirstIndex { get; set; }
        [Key(2)] public ulong FirstMonotonicDeltaUs { get; set; }
        [Key(3)] public CompactImuSample[] Records { get; set; } = [];

        public static CompactImuSegment From(RawImuSegment source) => new()
        {
            LocationId = source.LocationId,
            FirstIndex = source.FirstIndex,
            FirstMonotonicDeltaUs = source.FirstMonotonicDeltaUs,
            Records = source.Records.Select(value => new CompactImuSample(
                value.Ax,
                value.Ay,
                value.Az,
                value.Gx,
                value.Gy,
                value.Gz)).ToArray(),
        };
    }

    [MessagePackObject(AllowPrivate = true)]
    internal readonly record struct CompactImuSample(
        [property: Key(0)] short Ax,
        [property: Key(1)] short Ay,
        [property: Key(2)] short Az,
        [property: Key(3)] short Gx,
        [property: Key(4)] short Gy,
        [property: Key(5)] short Gz);
}

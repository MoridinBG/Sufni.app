using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class FixedRateSegmentBuilderTests
{
    [Fact]
    public void CreateSnapshot_SharesSealedChunksButKeepsOlderTailImmutable()
    {
        var builder = CreateBuilder(storageChunkSize: 2);
        AddValues(builder, 0, 1, 2);

        var olderSnapshot = builder.CreateSnapshot();

        AddValues(builder, 3, 4);
        var newerSnapshot = builder.CreateSnapshot();

        var olderSegment = Assert.Single(olderSnapshot);
        Assert.Equal([0, 1, 2], olderSegment.Values.ToArray());
        var newerSegment = Assert.Single(newerSnapshot);
        Assert.Equal([0, 1, 2, 3, 4], newerSegment.Values.ToArray());
    }

    [Fact]
    public void CreateSnapshot_FullTailRemainsImmutableAfterItBecomesSealed()
    {
        var builder = CreateBuilder(storageChunkSize: 2);
        AddValues(builder, 0, 1);

        var olderSnapshot = builder.CreateSnapshot();

        AddValues(builder, 2);

        Assert.Equal([0, 1], Assert.Single(olderSnapshot).Values.ToArray());
        Assert.Equal([0, 1, 2], Assert.Single(builder.CreateSnapshot()).Values.ToArray());
    }

    [Fact]
    public void StorageChunkBoundaries_DoNotCreateLogicalSegmentsOrGaps()
    {
        var gaps = new List<RawStreamGap>();
        var builder = new FixedRateSegmentBuilder<int>(
            streamKind: 1,
            locationId: null,
            gaps.Add,
            storageChunkSize: 2);

        AddValues(builder, 0, 1, 2, 3, 4);

        var segment = Assert.Single(builder.CreateSnapshot());
        Assert.Equal((ulong)0, segment.FirstIndex);
        Assert.Equal([0, 1, 2, 3, 4], segment.Values.ToArray());
        Assert.Empty(gaps);
    }

    [Fact]
    public void LogicalGap_RemainsVisibleAcrossInternalStorageChunks()
    {
        var gaps = new List<RawStreamGap>();
        var builder = new FixedRateSegmentBuilder<int>(
            streamKind: 1,
            locationId: null,
            gaps.Add,
            storageChunkSize: 2);
        AddValues(builder, 0, 1, 2, 3, 4);

        builder.AddInvalidRange(5, 2, 5_000, 1_000_000);
        builder.AddValidSample(7, 7_000, 7, 1_000_000);

        var snapshot = builder.CreateSnapshot();
        Assert.Equal(2, snapshot.Length);
        Assert.Equal([0, 1, 2, 3, 4], snapshot[0].Values.ToArray());
        Assert.Equal([7], snapshot[1].Values.ToArray());
        var gap = Assert.Single(gaps);
        Assert.Equal((ulong)5, gap.FirstMissingIndex);
        Assert.Equal((ulong)2, gap.MissingCount);
    }

    [Fact]
    public void Clear_ReplacesStorageWithoutChangingOlderSnapshot()
    {
        var builder = CreateBuilder(storageChunkSize: 2);
        AddValues(builder, 0, 1, 2, 3, 4);
        var olderSnapshot = builder.CreateSnapshot();

        builder.Clear();
        builder.AddValidSample(0, 10_000, 9, 1_000_000);

        Assert.Equal([0, 1, 2, 3, 4], Assert.Single(olderSnapshot).Values.ToArray());
        Assert.Equal([9], Assert.Single(builder.CreateSnapshot()).Values.ToArray());
        Assert.Equal((ulong)1, builder.Count);
    }

    [Fact]
    public void LatestEndMonotonicDeltaUs_UsesNominalSegmentTimingAcrossTimestampJitter()
    {
        const uint rateMhz = 3_000;
        const ulong firstMonotonicDeltaUs = 1_000_000;
        var builder = CreateBuilder();

        builder.AddValidSample(10, firstMonotonicDeltaUs, 1, rateMhz);
        builder.AddValidSample(11, 1_333_000, 2, rateMhz);

        var expectedEndMonotonicDeltaUs = firstMonotonicDeltaUs +
            SstV5CompactPayloadDecoder.RoundDurationUs(2, rateMhz);
        Assert.Equal(expectedEndMonotonicDeltaUs, builder.LatestEndMonotonicDeltaUs);
    }

    [Fact]
    public void LatestEndMonotonicDeltaUs_PreservesLatestValidEndAcrossGapsAndReset()
    {
        const uint rateMhz = 200_000;
        var builder = CreateBuilder();

        builder.AddValidSample(0, 10_000, 1, rateMhz);
        builder.AddInvalidRange(1, 1, 15_000, rateMhz);
        builder.AddValidSample(2, 1_000, 2, rateMhz);

        Assert.Equal((ulong)15_000, builder.LatestEndMonotonicDeltaUs);

        builder.Clear();

        Assert.Null(builder.LatestEndMonotonicDeltaUs);
        Assert.Equal((ulong)0, builder.Count);
    }

    private static FixedRateSegmentBuilder<int> CreateBuilder(int storageChunkSize = 1024) =>
        new(streamKind: 1, locationId: null, _ => { }, storageChunkSize);

    private static void AddValues(FixedRateSegmentBuilder<int> builder, params int[] values)
    {
        foreach (var value in values)
        {
            builder.AddValidSample((ulong)value, (ulong)value * 1_000, value, 1_000_000);
        }
    }
}

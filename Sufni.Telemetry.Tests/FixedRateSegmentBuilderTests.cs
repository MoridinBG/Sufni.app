using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class FixedRateSegmentBuilderTests
{
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

    private static FixedRateSegmentBuilder<int> CreateBuilder() =>
        new(streamKind: 1, locationId: null, _ => { });
}

using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class TelemetryStatisticsHighlightRangeTests
{
    [Fact]
    public void BinRange_FromBins_CapturesBoundariesAndMatchesSameBins()
    {
        double[] bins = [0, 10, 20];

        var range = TelemetryRangeSelection.BinRange.FromBins(bins, 1);

        Assert.Equal(1, range.Index);
        Assert.Equal(10, range.Start);
        Assert.Equal(20, range.End);
        Assert.False(range.IsFirst);
        Assert.True(range.IsLast);
        Assert.True(range.MatchesBins(bins));
        Assert.False(range.MatchesBins([0, 12, 20]));
    }

    [Fact]
    public void CalculateHighlightRanges_SampleAveragedDamping_ReturnsMatchingSampleRuns()
    {
        var telemetry = CreateTelemetryData(
            new Stroke
            {
                Start = 0,
                End = 5,
                DigitizedVelocity = [2, 2, 1, 2, 2, 2],
                DigitizedTravel = [0, 1, 4, 6, 8, 10],
                Stat = new StrokeStat
                {
                    Count = 6,
                    MaxTravel = 10,
                    MaxVelocity = 1.2,
                },
            });
        var selection = new DampingRangeSelection(
            SuspensionType.Front,
            VelocityAverageMode.SampleAveraged,
            VelocityBinIndex: 2,
            TravelBinStartIndex: 0,
            TravelBinEndIndex: 2);

        var ranges = TelemetryStatistics.CalculateHighlightRanges(telemetry, selection);

        Assert.Equal(
            [
                new TelemetryHighlightRange(0.0, 0.2),
            ],
            ranges);
    }

    [Fact]
    public void CalculateHighlightRanges_StrokeLength_ReturnsWholeMatchingStrokes()
    {
        var telemetry = CreateTelemetryData(
            new Stroke
            {
                Start = 0,
                End = 4,
                DigitizedVelocity = [1, 1, 1, 1, 1],
                DigitizedTravel = [0, 1, 2, 3, 4],
                Stat = new StrokeStat
                {
                    Count = 5,
                    MaxTravel = 15,
                    MaxVelocity = 1,
                },
            });
        var selection = new StrokeLengthRangeSelection(
            SuspensionType.Front,
            BalanceType.Compression,
            TelemetryRangeSelection.BinRange.FromBins([0, 10, 20, 30], 1));

        var ranges = TelemetryStatistics.CalculateHighlightRanges(telemetry, selection);

        Assert.Equal(
            [
                new TelemetryHighlightRange(0.0, 0.5),
            ],
            ranges);
    }

    [Fact]
    public void MergeHighlightRanges_MergesOnlyOverlappingRangesForSameSuspensionSide()
    {
        var ranges = new[]
        {
            new TelemetryHighlightRange(0.0, 0.5, SuspensionType.Front),
            new TelemetryHighlightRange(0.3, 0.8, SuspensionType.Front),
            new TelemetryHighlightRange(0.4, 0.7, SuspensionType.Rear),
            new TelemetryHighlightRange(1.0, 1.0, SuspensionType.Front),
        };

        var merged = TelemetryStatistics.MergeHighlightRanges(ranges);

        Assert.Equal(
            [
                new TelemetryHighlightRange(0.0, 0.8, SuspensionType.Front),
                new TelemetryHighlightRange(0.4, 0.7, SuspensionType.Rear),
            ],
            merged);
    }

    private static TelemetryData CreateTelemetryData(Stroke compression)
    {
        return new TelemetryData
        {
            Metadata = new Metadata
            {
                SourceName = "test",
                Version = 4,
                SampleRate = 10,
                Duration = 1,
            },
            Front = new Suspension
            {
                Present = true,
                MaxTravel = 40,
                Travel = [0, 5, 10, 12, 15, 15],
                Velocity = [0, 0.2, 0.4, 0.6, 0.8, 1.0],
                TravelBins = [0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200],
                VelocityBins = [-2, -1, 0, 1, 2],
                FineVelocityBins = [],
                Strokes = new Strokes
                {
                    Compressions = [compression],
                    Rebounds = [],
                },
            },
            Rear = new Suspension
            {
                Present = false,
                Travel = [],
                Velocity = [],
                TravelBins = [],
                VelocityBins = [],
                FineVelocityBins = [],
                Strokes = new Strokes
                {
                    Compressions = [],
                    Rebounds = [],
                },
            },
            Airtimes = [],
        };
    }
}

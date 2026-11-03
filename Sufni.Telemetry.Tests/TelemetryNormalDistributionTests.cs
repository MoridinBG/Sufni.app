namespace Sufni.Telemetry.Tests;

public class TelemetryNormalDistributionTests
{
    [Theory]
    [InlineData(false, -4.5, 6.25, 0.5714285714285714, 14.869047619047619)]
    [InlineData(true, -3.25, 2.75, 0, 9.1875)]
    public void CalculateNormalDistribution_UsesOnlyWholeActiveStrokes(
        bool selectRange, double minimum, double maximum, double mean, double variance)
    {
        var result = TelemetryStatistics.CalculateNormalDistribution(
            CreateTelemetry(), SuspensionType.Front,
            selectRange ? new TelemetryTimeRange(0.1, 0.65) : null);

        Assert.NotEmpty(result.Y);
        Assert.Equal(minimum, result.Y[0], 10);
        Assert.Equal(maximum, result.Y[^1], 10);
        Assert.Equal(result.Y.Count, result.Pdf.Count);
        Assert.All(result.Y.Zip(result.Y.Skip(1)), pair => Assert.True(pair.First < pair.Second));
        for (var index = 0; index < result.Y.Count; index++)
        {
            var velocity = result.Y[index];
            Assert.InRange(velocity, minimum, maximum);
            var expected = 10_000 * Math.Exp(-Math.Pow(velocity - mean, 2) / (2 * variance)) /
                Math.Sqrt(2 * Math.PI * variance);
            Assert.Equal(expected, result.Pdf[index], 8);
        }
    }

    private static TelemetryData CreateTelemetry()
    {
        var velocity = new[] { 900.0, -3.25, 0.5, 2.75, 800.0, 700.0, -4.5, -1.25, 3.5, 6.25 };
        var front = new Suspension
        {
            Present = true,
            Travel = [.. (new double[velocity.Length])],
            Velocity = [.. (velocity)],
            TravelBins = [0, 100],
            VelocityBins = [-100, 0, 100],
            Strokes = new Strokes
            {
                Compressions = [new Stroke { Start = 1, End = 3 }],
                Rebounds = [new Stroke { Start = 6, End = 9 }],
            },
        };
        return new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 10, Duration = 1 },
            Front = front,
            Rear = new Suspension
            {
                Travel = [],
                Velocity = [],
                TravelBins = [],
                VelocityBins = [],
                Strokes = new Strokes { Compressions = [], Rebounds = [] },
            },
            Airtimes = [],
            Markers = [],
        };
    }

}

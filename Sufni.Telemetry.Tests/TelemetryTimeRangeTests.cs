using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class TelemetryTimeRangeTests
{
    [Fact]
    public void Constructor_NormalizesReversedBoundaries()
    {
        var range = new TelemetryTimeRange(2.5, 1.0);

        Assert.Equal(1.0, range.StartSeconds);
        Assert.Equal(2.5, range.EndSeconds);
        Assert.Equal(1.5, range.DurationSeconds);
    }

    [Theory]
    [InlineData(1.0, 1.05)]
    [InlineData(double.NaN, 2.0)]
    [InlineData(1.0, double.PositiveInfinity)]
    public void Constructor_RejectsInvalidRanges(double startSeconds, double endSeconds)
    {
        Assert.Throws<ArgumentException>(() => new TelemetryTimeRange(startSeconds, endSeconds));
    }

    [Fact]
    public void TryCreate_NormalizesValidRange()
    {
        var created = TelemetryTimeRange.TryCreate(3.0, 1.0, out var range);

        Assert.True(created);
        Assert.Equal(1.0, range.StartSeconds);
        Assert.Equal(3.0, range.EndSeconds);
    }

    [Theory]
    [InlineData(1.0, 1.05)]
    [InlineData(double.NaN, 2.0)]
    [InlineData(1.0, double.NegativeInfinity)]
    public void TryCreate_ReturnsFalseForInvalidRange(double startSeconds, double endSeconds)
    {
        var created = TelemetryTimeRange.TryCreate(startSeconds, endSeconds, out var range);

        Assert.False(created);
        Assert.Equal(default, range);
    }

    [Fact]
    public void TryCreateClamped_ClampsRangeToDuration()
    {
        var created = TelemetryTimeRange.TryCreateClamped(-1.0, 3.0, 2.0, out var range);

        Assert.True(created);
        Assert.Equal(0.0, range.StartSeconds);
        Assert.Equal(2.0, range.EndSeconds);
    }

    [Theory]
    [InlineData(1.95, 3.0, 2.0)]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(0.0, 1.0, double.NaN)]
    public void TryCreateClamped_ReturnsFalseWhenClampedRangeIsInvalid(
        double startSeconds,
        double endSeconds,
        double durationSeconds)
    {
        var created = TelemetryTimeRange.TryCreateClamped(startSeconds, endSeconds, durationSeconds, out var range);

        Assert.False(created);
        Assert.Equal(default, range);
    }
}

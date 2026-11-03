using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class StrokesTests
{
    [Fact]
    public void Overlaps_WithOverlappingStrokes_ReturnsTrue()
    {
        // Arrange
        var s1 = new Stroke { Start = 0, End = 100 };
        var s2 = new Stroke { Start = 50, End = 150 };

        // Act
        var result = s1.Overlaps(s2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Overlaps_WithInsufficientOverlap_ReturnsFalse()
    {
        // Arrange
        var s1 = new Stroke { Start = 0, End = 100 };
        var s2 = new Stroke { Start = 80, End = 180 };

        // Act
        var result = s1.Overlaps(s2);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Overlaps_WithRealTimes_UsesTimeOverlapInsteadOfDenseIndexes()
    {
        var s1 = new Stroke { Start = 0, End = 100, StartSeconds = 0.0, EndSeconds = 1.0 };
        var s2 = new Stroke { Start = 0, End = 100, StartSeconds = 2.0, EndSeconds = 3.0 };

        var result = s1.Overlaps(s2);

        Assert.False(result);
    }

    [Fact]
    public void Analyze_SeparatesActiveStrokesAndOmitsIdling()
    {
        var result = StrokeAnalyzer.Analyze([100, 100, -100, -100, 0, 0], [0, 10, 10, 0, 5, 6], 100, 10);

        var compression = Assert.Single(result.Compressions);
        var rebound = Assert.Single(result.Rebounds);
        Assert.Equal((0, 1), (compression.Start, compression.End));
        Assert.Equal((2, 3), (rebound.Start, rebound.End));
        Assert.Equal(10, compression.Stat.SumTravel);
        Assert.Equal(-100, rebound.Stat.MaxVelocity);
    }
}

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
    public void Analyze_WithSineWave_DetectsAlternatingStrokes()
    {
        // Arrange
        int sampleRate = 1000;
        int count = 1000; 
        var velocity = new double[count];
        var travel = new double[count];
        
        // V(t) = sin(t).
        for (int i = 0; i < count; i++)
        {
            // Start slightly offset to avoid 0 velocity at start which seems to creates a 1 sample stroke
            double t = (i + 0.1) * 2 * Math.PI / count * 5; // 5 cycles
            velocity[i] = Math.Sin(t) * 100; 
            travel[i] = 50 + Math.Cos(t) * 10; 
        }

        // Act
        var result = StrokeAnalyzer.Analyze(velocity, travel, 100.0, sampleRate);
        var strokes = result.Compressions.Concat(result.Rebounds).OrderBy(stroke => stroke.Start).ToArray();

        // Assert
        // 5 cycles = 5 positive and 5 negative phases = 10 strokes
        Assert.Equal(10, strokes.Length);
        
        for (int i = 0; i < strokes.Length - 1; i++)
        {
            Assert.Equal(strokes[i].End, strokes[i+1].Start - 1);
        }
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

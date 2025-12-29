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
    public void FilterStrokes_WithSineWave_DetectsAlternatingStrokes()
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
        var strokes = Strokes.FilterStrokes(velocity, travel, 100.0, sampleRate);

        // Assert
        // 5 cycles = 5 positive and 5 negative phases = 10 strokes
        Assert.Equal(10, strokes.Length);
        
        for (int i = 0; i < strokes.Length - 1; i++)
        {
            Assert.Equal(strokes[i].End, strokes[i+1].Start - 1);
        }
    }

    [Fact]
    public void Categorize_SeparatesCompressionsAndRebounds()
    {
        // Arrange
        var strokesObj = new Strokes();
        var travel = new double[6];
        var velocity = new double[6];
        double maxTravel = 100.0;
        
        travel[0] = 0;
        travel[1] = 10;
        var comp = new Stroke(0, 1, 0.1, travel, velocity, maxTravel);
        
        travel[2] = 10;
        travel[3] = 0;
        var reb = new Stroke(2, 3, 0.1, travel, velocity, maxTravel);
        
        travel[4] = 5;
        travel[5] = 6;
        var idle = new Stroke(4, 5, 0.2, travel, velocity, maxTravel);

        var input = new[] { comp, reb, idle };

        // Act
        strokesObj.Categorize(input);

        // Assert
        Assert.Single(strokesObj.Compressions);
        Assert.Single(strokesObj.Rebounds);
        Assert.Single(strokesObj.Idlings);
        
        Assert.Equal(comp, strokesObj.Compressions[0]);
        Assert.Equal(reb, strokesObj.Rebounds[0]);
        Assert.Equal(idle, strokesObj.Idlings[0]);
    }

    [Fact]
    public void Categorize_IdentifiesAirCandidates()
    {
        // Arrange
        var strokesObj = new Strokes();
        var travel = new double[6];
        var velocity = new double[6];
        double maxTravel = 100.0;
        
        travel[0] = 0;
        travel[1] = 10;
        var prev = new Stroke(0, 1, 0.01, travel, velocity, maxTravel);
        
        travel[2] = 2;
        travel[3] = 3;
        var idle = new Stroke(2, 3, 0.2, travel, velocity, maxTravel);
        
        travel[4] = 3;
        travel[5] = 15;
        velocity[5] = 600;
        var next = new Stroke(4, 5, 0.01, travel, velocity, maxTravel);

        var input = new[] { prev, idle, next };

        // Act
        strokesObj.Categorize(input);

        // Assert
        Assert.True(idle.AirCandidate);
    }
}
using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class StrokeAnalyzerTests
{
    [Fact]
    public void Analyze_WithFlatTrace_ReturnsNoActiveStrokes()
    {
        var velocity = new double[100];
        var travel = new double[100];
        Array.Fill(travel, 10);
        var digitized = new int[100];

        var result = StrokeAnalyzer.Analyze(
            velocity,
            travel,
            maxTravel: 20,
            sampleRate: 1000,
            digitized,
            digitized,
            digitized);

        Assert.False(result.HasActiveStrokes);
        Assert.Empty(result.Strokes.Compressions);
        Assert.Empty(result.Strokes.Rebounds);
    }
}

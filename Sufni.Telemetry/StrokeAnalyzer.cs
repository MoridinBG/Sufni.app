namespace Sufni.Telemetry;

public readonly record struct StrokeAnalysisResult(
    bool HasActiveStrokes,
    Strokes Strokes);

public static class StrokeAnalyzer
{
    public static StrokeAnalysisResult Analyze(
        double[] velocity,
        double[] travel,
        double maxTravel,
        int sampleRate)
    {
        var strokes = new Strokes();
        var detectedStrokes = Strokes.FilterStrokes(velocity, travel, maxTravel, sampleRate);
        strokes.Categorize(detectedStrokes);

        var hasActiveStrokes = strokes.Compressions.Length != 0 || strokes.Rebounds.Length != 0;
        return new StrokeAnalysisResult(hasActiveStrokes, strokes);
    }
}

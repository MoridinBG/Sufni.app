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
        int sampleRate,
        int[] digitizedTravel,
        int[] digitizedVelocity,
        int[] fineDigitizedVelocity)
    {
        var strokes = new Strokes();
        var detectedStrokes = Strokes.FilterStrokes(velocity, travel, maxTravel, sampleRate);
        strokes.Categorize(detectedStrokes);

        var hasActiveStrokes = strokes.Compressions.Length != 0 || strokes.Rebounds.Length != 0;
        if (hasActiveStrokes)
        {
            strokes.Digitize(digitizedTravel, digitizedVelocity, fineDigitizedVelocity);
        }

        return new StrokeAnalysisResult(hasActiveStrokes, strokes);
    }
}

namespace Sufni.Telemetry;

public static class StrokeAnalyzer
{
    public static Strokes Analyze(
        double[] velocity,
        double[] travel,
        double maxTravel,
        int sampleRate)
    {
        var strokes = new Strokes();
        var detectedStrokes = Strokes.FilterStrokes(velocity, travel, maxTravel, sampleRate);
        strokes.Categorize(detectedStrokes);

        return strokes;
    }
}

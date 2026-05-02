namespace Sufni.Telemetry;

public sealed record ProcessedSuspensionTrace(
    bool Present,
    double[] Travel,
    double[] Velocity,
    Strokes Strokes,
    double[] TravelBins,
    double[] VelocityBins,
    double[] FineVelocityBins);

public static class SuspensionTraceProcessor
{
    public static ProcessedSuspensionTrace Process(
        ushort[] measurements,
        double maxTravel,
        Func<ushort, double> measurementToTravel,
        int sampleRate,
        double[] time,
        SavitzkyGolay velocityFilter)
    {
        var travel = CalculateTravel(measurements, maxTravel, measurementToTravel);
        var travelBins = HistogramBuilder.Linspace(0, maxTravel, Parameters.TravelHistBins + 1);
        var digitizedTravel = HistogramBuilder.Digitize(travel, travelBins);

        var velocity = velocityFilter.Process(travel, time);
        var velocityBins = HistogramBuilder.DigitizeVelocity(velocity, Parameters.VelocityHistStep);
        var fineVelocityBins = HistogramBuilder.DigitizeVelocity(velocity, Parameters.VelocityHistStepFine);

        var strokeAnalysis = StrokeAnalyzer.Analyze(
            velocity,
            travel,
            maxTravel,
            sampleRate,
            digitizedTravel,
            velocityBins.Values,
            fineVelocityBins.Values);

        return new ProcessedSuspensionTrace(
            strokeAnalysis.HasActiveStrokes,
            travel,
            velocity,
            strokeAnalysis.Strokes,
            travelBins,
            velocityBins.Bins,
            fineVelocityBins.Bins);
    }

    private static double[] CalculateTravel(
        ushort[] measurements,
        double maxTravel,
        Func<ushort, double> measurementToTravel)
    {
        var travel = new double[measurements.Length];
        for (var index = 0; index < measurements.Length; index++)
        {
            travel[index] = Math.Clamp(measurementToTravel(measurements[index]), 0, maxTravel);
        }

        return travel;
    }
}

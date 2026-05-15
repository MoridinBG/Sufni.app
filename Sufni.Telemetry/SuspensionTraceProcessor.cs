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
        SavitzkyGolay? velocityFilter)
    {
        var travel = CalculateTravel(measurements, maxTravel, measurementToTravel);
        var travelBins = HistogramBuilder.Linspace(0, maxTravel, Parameters.TravelHistBins + 1);
        var digitizedTravel = HistogramBuilder.Digitize(travel, travelBins);

        var velocity = velocityFilter is null
            ? CalculateUnfilteredVelocity(travel, time)
            : velocityFilter.Process(travel, time);
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

    private static double[] CalculateUnfilteredVelocity(double[] travel, double[] time)
    {
        var velocity = new double[travel.Length];
        if (travel.Length < 2)
        {
            return velocity;
        }

        velocity[0] = CalculateSlope(travel[0], travel[1], time[0], time[1]);
        for (var index = 1; index < travel.Length - 1; index++)
        {
            velocity[index] = CalculateSlope(
                travel[index - 1],
                travel[index + 1],
                time[index - 1],
                time[index + 1]);
        }

        var last = travel.Length - 1;
        velocity[last] = CalculateSlope(travel[last - 1], travel[last], time[last - 1], time[last]);
        return velocity;
    }

    private static double CalculateSlope(double startValue, double endValue, double startTime, double endTime)
    {
        var deltaTime = endTime - startTime;
        return double.IsNaN(deltaTime) || double.IsInfinity(deltaTime) || deltaTime <= 0
            ? 0
            : (endValue - startValue) / deltaTime;
    }
}

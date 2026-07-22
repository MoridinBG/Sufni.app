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
        SavitzkyGolay? velocityFilter)
    {
        var travel = CalculateTravel(measurements, maxTravel, measurementToTravel);
        var travelBins = HistogramBuilder.Linspace(0, maxTravel, Parameters.TravelHistBins + 1);
        var dt = 1.0 / sampleRate;

        var velocity = velocityFilter is null
            ? CalculateUnfilteredVelocity(travel, dt)
            : velocityFilter.Process(travel, dt);
        var velocityBins = HistogramBuilder.CreateVelocityBins(velocity, Parameters.VelocityHistStep);
        var fineVelocityBins = HistogramBuilder.CreateVelocityBins(velocity, Parameters.VelocityHistStepFine);

        var strokeAnalysis = StrokeAnalyzer.Analyze(
            velocity,
            travel,
            maxTravel,
            sampleRate);

        return new ProcessedSuspensionTrace(
            strokeAnalysis.HasActiveStrokes,
            travel,
            velocity,
            strokeAnalysis.Strokes,
            travelBins,
            velocityBins,
            fineVelocityBins);
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

    private static double[] CalculateUnfilteredVelocity(double[] travel, double dt)
    {
        var velocity = new double[travel.Length];
        if (travel.Length < 2)
        {
            return velocity;
        }

        velocity[0] = CalculateSlope(travel[0], travel[1], dt);
        for (var index = 1; index < travel.Length - 1; index++)
        {
            velocity[index] = CalculateSlope(
                travel[index - 1],
                travel[index + 1],
                2 * dt);
        }

        var last = travel.Length - 1;
        velocity[last] = CalculateSlope(travel[last - 1], travel[last], dt);
        return velocity;
    }

    private static double CalculateSlope(double startValue, double endValue, double deltaTime)
    {
        return double.IsNaN(deltaTime) || double.IsInfinity(deltaTime) || deltaTime <= 0
            ? 0
            : (endValue - startValue) / deltaTime;
    }
}

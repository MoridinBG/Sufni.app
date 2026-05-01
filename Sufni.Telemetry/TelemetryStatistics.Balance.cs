using System.Diagnostics;
using MathNet.Numerics;

namespace Sufni.Telemetry;

public static partial class TelemetryStatistics
{
    public static bool HasBalanceData(
        TelemetryData telemetryData,
        BalanceType type,
        TelemetryTimeRange? range = null)
    {
        return HasBalanceData(telemetryData, type, new BalanceStatisticsOptions(range));
    }

    public static bool HasBalanceData(
        TelemetryData telemetryData,
        BalanceType type,
        BalanceStatisticsOptions options)
    {
        if (!HasStrokeData(telemetryData, SuspensionType.Front, options.Range) ||
            !HasStrokeData(telemetryData, SuspensionType.Rear, options.Range))
        {
            return false;
        }

        var frontTravelVelocity = TravelVelocity(telemetryData, SuspensionType.Front, type, options);
        var rearTravelVelocity = TravelVelocity(telemetryData, SuspensionType.Rear, type, options);

        return frontTravelVelocity.Item1.Length >= 2 && rearTravelVelocity.Item1.Length >= 2;
    }

    public static BalanceData CalculateBalance(
        TelemetryData telemetryData,
        BalanceType type,
        TelemetryTimeRange? range = null)
    {
        return CalculateBalance(telemetryData, type, new BalanceStatisticsOptions(range));
    }

    public static BalanceData CalculateBalance(
        TelemetryData telemetryData,
        BalanceType type,
        BalanceStatisticsOptions options)
    {
        var frontTravelVelocity = TravelVelocity(telemetryData, SuspensionType.Front, type, options);
        var rearTravelVelocity = TravelVelocity(telemetryData, SuspensionType.Rear, type, options);

        if (frontTravelVelocity.Item1.Length == 0 || rearTravelVelocity.Item1.Length == 0)
        {
            return new BalanceData([], [], [], [], [], [], 0);
        }

        var frontTrendLine = FitLinearTrend(frontTravelVelocity.Item1, frontTravelVelocity.Item2);
        var rearTrendLine = FitLinearTrend(rearTravelVelocity.Item1, rearTravelVelocity.Item2);

        var frontTrend = frontTravelVelocity.Item1.Select(frontTrendLine.Predict).ToList();
        var rearTrend = rearTravelVelocity.Item1.Select(rearTrendLine.Predict).ToList();

        var pairedCount = Math.Min(frontTrend.Count, rearTrend.Count);
        var sum = frontTrend.Zip(rearTrend, (front, rear) => front - rear).Sum();
        var meanSignedDeviation = pairedCount == 0 ? 0 : sum / pairedCount;
        var signedSlopeDeltaPercent = CalculateSlopeDeltaPercent(frontTrendLine.Slope, rearTrendLine.Slope);

        return new BalanceData(
            [.. frontTravelVelocity.Item1],
            [.. frontTravelVelocity.Item2],
            frontTrend,
            [.. rearTravelVelocity.Item1],
            [.. rearTravelVelocity.Item2],
            rearTrend,
            meanSignedDeviation,
            frontTrendLine.Slope,
            rearTrendLine.Slope,
            signedSlopeDeltaPercent,
            Math.Abs(signedSlopeDeltaPercent));
    }

    private readonly record struct LinearTrend(double Slope, double Intercept)
    {
        public double Predict(double x) => Slope * x + Intercept;
    }

    private static LinearTrend FitLinearTrend(double[] x, double[] y)
    {
        if (x.Length == 0 || y.Length == 0)
        {
            return new LinearTrend(0, 0);
        }

        if (x.Length == 1 || y.Length == 1 || x.Distinct().Count() == 1)
        {
            return new LinearTrend(0, y.Average());
        }

        var coefficients = Fit.Polynomial(x, y, 1);
        return new LinearTrend(coefficients[1], coefficients[0]);
    }

    private static (double[], double[]) TravelVelocity(
        TelemetryData telemetryData,
        SuspensionType suspensionType,
        BalanceType balanceType,
        BalanceStatisticsOptions options)
    {
        Debug.Assert(suspensionType != SuspensionType.Front || telemetryData.Front.MaxTravel is not null);
        Debug.Assert(suspensionType != SuspensionType.Rear || telemetryData.Rear.MaxTravel is not null);

        var suspension = GetSuspension(telemetryData, suspensionType);
        var travelMax = suspensionType == SuspensionType.Front
            ? telemetryData.Front.MaxTravel!.Value
            : telemetryData.Rear.MaxTravel!.Value;
        var strokes = GetIncludedStrokes(telemetryData, suspension, balanceType, options.Range);

        var travelValues = new List<double>();
        var velocityValues = new List<double>();

        foreach (var stroke in strokes)
        {
            var travel = options.DisplacementMode switch
            {
                BalanceDisplacementMode.Travel => Math.Abs(suspension.Travel[stroke.End] - suspension.Travel[stroke.Start]),
                BalanceDisplacementMode.Zenith => stroke.Stat.MaxTravel,
                _ => throw new ArgumentOutOfRangeException(nameof(options), options.DisplacementMode, null),
            };

            travelValues.Add(travel / travelMax * 100);
            velocityValues.Add(balanceType == BalanceType.Rebound ? -stroke.Stat.MaxVelocity : stroke.Stat.MaxVelocity);
        }

        var travelArray = travelValues.ToArray();
        var velocityArray = velocityValues.ToArray();

        Array.Sort(travelArray, velocityArray);

        return (travelArray, velocityArray);
    }

    private static double CalculateSlopeDeltaPercent(double frontSlope, double rearSlope)
    {
        var denominator = Math.Max(Math.Abs(frontSlope), Math.Abs(rearSlope));
        return denominator < 1e-9 ? 0 : (frontSlope - rearSlope) / denominator * 100.0;
    }
}

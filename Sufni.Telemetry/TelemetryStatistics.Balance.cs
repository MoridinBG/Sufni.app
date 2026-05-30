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

        var supportsTrend = options.DisplacementMode != BalanceDisplacementMode.Speed;
        var frontTrend = supportsTrend
            ? frontTravelVelocity.Item1.Select(frontTrendLine.Predict).ToList()
            : [];
        var rearTrend = supportsTrend
            ? rearTravelVelocity.Item1.Select(rearTrendLine.Predict).ToList()
            : [];

        var pairedCount = supportsTrend ? Math.Min(frontTrend.Count, rearTrend.Count) : 0;
        var sum = supportsTrend ? frontTrend.Zip(rearTrend, (front, rear) => front - rear).Sum() : 0;
        var meanSignedDeviation = pairedCount == 0 ? 0 : sum / pairedCount;
        var frontSlope = supportsTrend ? frontTrendLine.Slope : 0;
        var rearSlope = supportsTrend ? rearTrendLine.Slope : 0;
        var signedSlopeDeltaPercent = supportsTrend ? CalculateSlopeDeltaPercent(frontSlope, rearSlope) : 0;

        return new BalanceData(
            [.. frontTravelVelocity.Item1],
            [.. frontTravelVelocity.Item2],
            frontTrend,
            [.. rearTravelVelocity.Item1],
            [.. rearTravelVelocity.Item2],
            rearTrend,
            meanSignedDeviation,
            frontSlope,
            rearSlope,
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
        var highSpeedThreshold = HighSpeedThresholdFor(suspensionType, balanceType, options);
        var strokes = GetIncludedStrokes(telemetryData, suspension, balanceType, options.Range)
            .Where(stroke => IncludesSpeedMode(stroke, options.SpeedMode, highSpeedThreshold))
            .ToArray();

        var travelValues = new List<double>();
        var velocityValues = new List<double>();

        foreach (var stroke in strokes)
        {
            var travel = options.DisplacementMode switch
            {
                BalanceDisplacementMode.Travel => Math.Abs(suspension.Travel[stroke.End] - suspension.Travel[stroke.Start]),
                BalanceDisplacementMode.Zenith => stroke.Stat.MaxTravel,
                BalanceDisplacementMode.Speed => GetPeakSpeedTravel(suspension, stroke, balanceType),
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

    private static double GetPeakSpeedTravel(
        Suspension suspension,
        Stroke stroke,
        BalanceType balanceType)
    {
        if (suspension.Travel.Length == 0)
        {
            return 0;
        }

        var start = Math.Clamp(stroke.Start, 0, suspension.Travel.Length - 1);
        var end = Math.Clamp(stroke.End, 0, suspension.Travel.Length - 1);
        if (end < start || suspension.Velocity.Length == 0)
        {
            return suspension.Travel[start];
        }

        end = Math.Min(end, suspension.Velocity.Length - 1);
        if (end < start)
        {
            return suspension.Travel[start];
        }

        var peakIndex = start;
        var peakVelocity = suspension.Velocity[start];
        for (var index = start + 1; index <= end; index++)
        {
            var velocity = suspension.Velocity[index];
            if (balanceType == BalanceType.Rebound
                    ? velocity < peakVelocity
                    : velocity > peakVelocity)
            {
                peakVelocity = velocity;
                peakIndex = index;
            }
        }

        return suspension.Travel[Math.Clamp(peakIndex, 0, suspension.Travel.Length - 1)];
    }

    private static double CalculateSlopeDeltaPercent(double frontSlope, double rearSlope)
    {
        var denominator = Math.Max(Math.Abs(frontSlope), Math.Abs(rearSlope));
        return denominator < 1e-9 ? 0 : (frontSlope - rearSlope) / denominator * 100.0;
    }

    private static double HighSpeedThresholdFor(
        SuspensionType suspensionType,
        BalanceType balanceType,
        BalanceStatisticsOptions options)
    {
        return (suspensionType, balanceType) switch
        {
            (SuspensionType.Front, BalanceType.Compression) => options.FrontCompressionHighSpeedThreshold,
            (SuspensionType.Front, BalanceType.Rebound) => options.FrontReboundHighSpeedThreshold,
            (SuspensionType.Rear, BalanceType.Compression) => options.RearCompressionHighSpeedThreshold,
            (SuspensionType.Rear, BalanceType.Rebound) => options.RearReboundHighSpeedThreshold,
            _ => throw new ArgumentOutOfRangeException(nameof(balanceType), balanceType, null),
        };
    }

    private static bool IncludesSpeedMode(
        Stroke stroke,
        BalanceSpeedMode speedMode,
        double highSpeedThreshold)
    {
        return speedMode switch
        {
            BalanceSpeedMode.Both => true,
            BalanceSpeedMode.LowSpeed => Math.Abs(stroke.Stat.MaxVelocity) < highSpeedThreshold,
            BalanceSpeedMode.HighSpeed => Math.Abs(stroke.Stat.MaxVelocity) >= highSpeedThreshold,
            _ => throw new ArgumentOutOfRangeException(nameof(speedMode), speedMode, null),
        };
    }
}

using System;
using System.Collections.Generic;
using ScottPlot;

namespace Sufni.App.Plots;

internal sealed class BalanceTrendReadout : IPointerReadoutTarget
{
    private BalanceTrendReadout(
        IReadOnlyList<double> frontXValues,
        IReadOnlyList<double> frontYValues,
        IReadOnlyList<double> rearXValues,
        IReadOnlyList<double> rearYValues,
        string xLabel)
    {
        FrontXValues = frontXValues;
        FrontYValues = frontYValues;
        RearXValues = rearXValues;
        RearYValues = rearYValues;
        XLabel = xLabel;
    }

    private IReadOnlyList<double> FrontXValues { get; }
    private IReadOnlyList<double> FrontYValues { get; }
    private IReadOnlyList<double> RearXValues { get; }
    private IReadOnlyList<double> RearYValues { get; }
    private string XLabel { get; }

    public static BalanceTrendReadout FromTrends(
        IReadOnlyList<double> frontXValues,
        IReadOnlyList<double> frontYValues,
        IReadOnlyList<double> rearXValues,
        IReadOnlyList<double> rearYValues,
        string xLabel)
    {
        return new BalanceTrendReadout(frontXValues, frontYValues, rearXValues, rearYValues, xLabel);
    }

    public double GetDistanceSquared(Coordinates pointer, Plot plot)
    {
        return 0;
    }

    public CursorReadout ToCursorReadout(Coordinates pointer, Plot plot)
    {
        var lines = new List<CursorReadoutLine>
        {
            new(XLabel, pointer.X, "%", Colors.LightGray, "0.#")
        };

        var frontValue = InterpolateAt(FrontXValues, FrontYValues, pointer.X);
        if (double.IsFinite(frontValue))
        {
            lines.Add(new CursorReadoutLine("Front peak speed", frontValue, "mm/s", TelemetryPlot.FrontColor, "0.#"));
        }

        var rearValue = InterpolateAt(RearXValues, RearYValues, pointer.X);
        if (double.IsFinite(rearValue))
        {
            lines.Add(new CursorReadoutLine("Rear peak speed", rearValue, "mm/s", TelemetryPlot.RearColor, "0.#"));
        }

        return new CursorReadout(
            double.NaN,
            pointer.X,
            pointer.Y,
            lines,
            Header: null,
            KeepTooltipInsideDataArea: true);
    }

    private static double InterpolateAt(IReadOnlyList<double> xValues, IReadOnlyList<double> yValues, double x)
    {
        var count = Math.Min(xValues.Count, yValues.Count);
        if (count == 0)
        {
            return double.NaN;
        }

        if (count == 1)
        {
            return yValues[0];
        }

        for (var index = 0; index < count - 1; index++)
        {
            var x1 = xValues[index];
            var x2 = xValues[index + 1];
            if (!IsBetween(x, x1, x2) || x1 == x2)
            {
                continue;
            }

            return Interpolate(x1, yValues[index], x2, yValues[index + 1], x);
        }

        if (x < xValues[0] && TryGetDistinctSegment(xValues, yValues, 0, count, 1, out var lowSegment))
        {
            return Interpolate(lowSegment.X1, lowSegment.Y1, lowSegment.X2, lowSegment.Y2, x);
        }

        if (x > xValues[count - 1] && TryGetDistinctSegment(xValues, yValues, count - 1, -1, -1, out var highSegment))
        {
            return Interpolate(highSegment.X1, highSegment.Y1, highSegment.X2, highSegment.Y2, x);
        }

        return yValues[GetNearestIndex(xValues, count, x)];
    }

    private static bool IsBetween(double value, double first, double second)
    {
        return value >= Math.Min(first, second) && value <= Math.Max(first, second);
    }

    private static double Interpolate(double x1, double y1, double x2, double y2, double x)
    {
        return y1 + (x - x1) / (x2 - x1) * (y2 - y1);
    }

    private static bool TryGetDistinctSegment(
        IReadOnlyList<double> xValues,
        IReadOnlyList<double> yValues,
        int startIndex,
        int endIndex,
        int step,
        out Segment segment)
    {
        var count = Math.Min(xValues.Count, yValues.Count);
        for (var index = startIndex; index != endIndex; index += step)
        {
            var next = index + step;
            if (next < 0 || next >= count || xValues[index] == xValues[next])
            {
                continue;
            }

            segment = new Segment(xValues[index], yValues[index], xValues[next], yValues[next]);
            return true;
        }

        segment = default;
        return false;
    }

    private static int GetNearestIndex(IReadOnlyList<double> xValues, int count, double x)
    {
        var nearestIndex = 0;
        var nearestDistance = double.PositiveInfinity;
        for (var index = 0; index < count; index++)
        {
            var distance = Math.Abs(xValues[index] - x);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = index;
            }
        }

        return nearestIndex;
    }

    private readonly record struct Segment(double X1, double Y1, double X2, double Y2);
}

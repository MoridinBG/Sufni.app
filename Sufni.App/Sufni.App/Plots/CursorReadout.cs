using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ScottPlot;

namespace Sufni.App.Plots;

public sealed record CursorReadoutLine(
    string Label,
    double Value,
    string Unit,
    Color Color,
    string Format = "0.##")
{
    public string FormatValue()
    {
        var formatted = Value.ToString(Format, CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(Unit)
            ? formatted
            : $"{formatted} {Unit}";
    }
}

public sealed record CursorReadout(
    double TimeSeconds,
    double AnchorX,
    double AnchorY,
    IReadOnlyList<CursorReadoutLine> Lines)
{
    public Color AccentColor => Lines.FirstOrDefault()?.Color ?? Colors.LightGray;
}

public sealed class CursorReadoutSeries
{
    private readonly IReadOnlyList<double>? xValues;
    private readonly IReadOnlyList<double> yValues;
    private readonly double? regularStep;
    private readonly double maximumX;
    private readonly bool xValuesSorted;

    private CursorReadoutSeries(
        string label,
        string unit,
        Color color,
        string format,
        IReadOnlyList<double>? xValues,
        IReadOnlyList<double> yValues,
        double? regularStep,
        double maximumX)
    {
        Label = label;
        Unit = unit;
        Color = color;
        Format = format;
        this.xValues = xValues;
        this.yValues = yValues;
        this.regularStep = regularStep;
        this.maximumX = maximumX;
        xValuesSorted = xValues is not null && IsSortedAscending(xValues);
    }

    public string Label { get; }
    public string Unit { get; }
    public Color Color { get; }
    public string Format { get; }

    public static CursorReadoutSeries FromRegularSamples(
        string label,
        string unit,
        Color color,
        IReadOnlyList<double> yValues,
        double step,
        double maximumX,
        string format = "0.##")
    {
        var finiteStep = double.IsFinite(step) && step > 0 ? step : 1.0;
        return new CursorReadoutSeries(label, unit, color, format, null, yValues, finiteStep, maximumX);
    }

    public static CursorReadoutSeries FromScatterSamples(
        string label,
        string unit,
        Color color,
        IReadOnlyList<double> xValues,
        IReadOnlyList<double> yValues,
        string format = "0.##")
    {
        return new CursorReadoutSeries(label, unit, color, format, xValues, yValues, null, 0);
    }

    public bool TryGetLine(double position, out CursorReadoutLine line)
    {
        line = default!;
        if (!double.IsFinite(position) || yValues.Count == 0)
        {
            return false;
        }

        var index = GetNearestIndex(position);
        if (index < 0 || index >= yValues.Count)
        {
            return false;
        }

        var value = yValues[index];
        if (!double.IsFinite(value))
        {
            return false;
        }

        line = new CursorReadoutLine(Label, value, Unit, Color, Format);
        return true;
    }

    private int GetNearestIndex(double position)
    {
        if (regularStep is { } step)
        {
            return GetNearestRegularIndex(position, step);
        }

        if (xValues is null || xValues.Count == 0)
        {
            return -1;
        }

        if (xValuesSorted)
        {
            return GetNearestSortedIndex(position);
        }

        return GetNearestUnsortedIndex(position);
    }

    private int GetNearestRegularIndex(double position, double step)
    {
        if (position < 0 || position > maximumX)
        {
            return -1;
        }

        var index = (int)Math.Round(position / step, MidpointRounding.AwayFromZero);
        return Math.Clamp(index, 0, yValues.Count - 1);
    }

    private int GetNearestSortedIndex(double position)
    {
        if (xValues is null)
        {
            return -1;
        }

        if (position <= xValues[0])
        {
            return 0;
        }

        if (position >= xValues[^1])
        {
            return xValues.Count - 1;
        }

        var low = 0;
        var high = xValues.Count - 1;
        while (low <= high)
        {
            var mid = low + (high - low) / 2;
            var midValue = xValues[mid];
            if (position < midValue)
            {
                high = mid - 1;
            }
            else if (position > midValue)
            {
                low = mid + 1;
            }
            else
            {
                return mid;
            }
        }

        if (low >= xValues.Count)
        {
            return xValues.Count - 1;
        }

        if (high < 0)
        {
            return 0;
        }

        return Math.Abs(position - xValues[high]) <= Math.Abs(xValues[low] - position)
            ? high
            : low;
    }

    private int GetNearestUnsortedIndex(double position)
    {
        if (xValues is null)
        {
            return -1;
        }

        var nearestIndex = 0;
        var nearestDistance = double.PositiveInfinity;
        for (var index = 0; index < xValues.Count; index++)
        {
            var distance = Math.Abs(position - xValues[index]);
            if (distance < nearestDistance)
            {
                nearestIndex = index;
                nearestDistance = distance;
            }
        }

        return nearestIndex;
    }

    private static bool IsSortedAscending(IReadOnlyList<double> values)
    {
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index] < values[index - 1])
            {
                return false;
            }
        }

        return true;
    }
}

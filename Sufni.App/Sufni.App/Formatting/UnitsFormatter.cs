using System;
using System.Globalization;

namespace Sufni.App.Formatting;

internal static class UnitsFormatter
{
    public static string FormatDuration(TimeSpan duration)
    {
        var roundedSeconds = Math.Max(0, (long)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero));
        var hours = roundedSeconds / 3600;
        var minutes = roundedSeconds % 3600 / 60;
        var seconds = roundedSeconds % 60;

        if (hours > 0)
        {
            return $"{FormatWhole(hours)}h {minutes.ToString("00", CultureInfo.InvariantCulture)}m";
        }

        if (minutes > 0)
        {
            return $"{FormatWhole(minutes)}m {seconds.ToString("00", CultureInfo.InvariantCulture)}s";
        }

        return $"{FormatWhole(seconds)}s";
    }

    public static string FormatDistance(double meters)
    {
        return meters >= 1000
            ? $"{FormatNumber(meters / 1000.0, 1)} km"
            : $"{FormatWhole(Math.Round(meters, MidpointRounding.AwayFromZero))} m";
    }

    public static string FormatSpeed(double mmPerSecond)
    {
        return FormatNumber(mmPerSecond, 0);
    }

    public static string FormatNumber(double value, int decimals)
    {
        return value.ToString($"F{decimals}", CultureInfo.InvariantCulture);
    }

    private static string FormatWhole(double value)
    {
        return value.ToString("0", CultureInfo.InvariantCulture);
    }
}

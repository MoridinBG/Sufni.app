using System;
using System.Globalization;

namespace Sufni.App.Formatting;

internal static class UnitsFormatter
{
    // Display call sites use the default (current culture); hash/persistence
    // call paths must pass CultureInfo.InvariantCulture explicitly.
    public static string FormatDuration(TimeSpan duration, IFormatProvider? provider = null)
    {
        var culture = provider ?? CultureInfo.CurrentCulture;
        var roundedSeconds = Math.Max(0, (long)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero));
        var hours = roundedSeconds / 3600;
        var minutes = roundedSeconds % 3600 / 60;
        var seconds = roundedSeconds % 60;

        if (hours > 0)
        {
            return $"{FormatWhole(hours, culture)}h {minutes.ToString("00", culture)}m";
        }

        if (minutes > 0)
        {
            return $"{FormatWhole(minutes, culture)}m {seconds.ToString("00", culture)}s";
        }

        return $"{FormatWhole(seconds, culture)}s";
    }

    public static string FormatDistance(double meters, IFormatProvider? provider = null)
    {
        return meters >= 1000
            ? $"{FormatNumber(meters / 1000.0, 1, provider)} km"
            : $"{FormatWhole(Math.Round(meters, MidpointRounding.AwayFromZero), provider ?? CultureInfo.CurrentCulture)} m";
    }

    public static string FormatSpeed(double mmPerSecond, IFormatProvider? provider = null)
    {
        return FormatNumber(mmPerSecond, 0, provider);
    }

    public static string FormatNumber(double value, int decimals, IFormatProvider? provider = null)
    {
        return value.ToString($"F{decimals}", provider ?? CultureInfo.CurrentCulture);
    }

    private static string FormatWhole(double value, IFormatProvider provider)
    {
        return value.ToString("0", provider);
    }
}

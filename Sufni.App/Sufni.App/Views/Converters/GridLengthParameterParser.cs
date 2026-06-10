using System;
using System.Globalization;
using Avalonia.Controls;

namespace Sufni.App.Views.Converters;

internal static class GridLengthParameterParser
{
    public static GridLength Parse(object? parameter, string defaultValue = "Auto")
    {
        var value = parameter?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = defaultValue;
        }

        if (value.EndsWith('*'))
        {
            var starValue = value.Length > 1
                ? double.Parse(value[..^1], CultureInfo.InvariantCulture)
                : 1;
            return new GridLength(starValue, GridUnitType.Star);
        }

        if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            return GridLength.Auto;
        }

        return new GridLength(double.Parse(value, CultureInfo.InvariantCulture), GridUnitType.Pixel);
    }
}

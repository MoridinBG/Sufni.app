using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Sufni.App.Views.Controls;

public sealed class ImportActionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            true => 1,
            null => 2,
            _ => 0,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            1 => (bool?)true,
            2 => (bool?)null,
            _ => (bool?)false,
        };
}

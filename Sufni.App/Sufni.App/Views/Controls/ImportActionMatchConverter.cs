using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Sufni.App.Views.Controls;

public sealed class ImportActionMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var index = value as int? ?? -1;
        return (parameter as string) switch
        {
            "Import" => index == 1,
            "Trash" => index == 2,
            _ => false,
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

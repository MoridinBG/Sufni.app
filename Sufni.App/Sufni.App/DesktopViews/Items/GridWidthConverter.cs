using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Sufni.App.DesktopViews.Items;

public class GridWidthConverter : IValueConverter
{
    public static readonly GridWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            double length => new GridLength(length, GridUnitType.Pixel),
            null => new GridLength(0, GridUnitType.Auto),
            _ => new BindingNotification(new InvalidCastException(), BindingErrorType.Error)
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

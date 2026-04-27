using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Sufni.App.Presentation;

namespace Sufni.App.Views.Controls;

public sealed class SurfaceStateToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SurfacePresentationState { ReservesLayout: true })
        {
            return GridLength.Parse(parameter?.ToString() ?? "Auto");
        }

        return new GridLength(0, GridUnitType.Pixel);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
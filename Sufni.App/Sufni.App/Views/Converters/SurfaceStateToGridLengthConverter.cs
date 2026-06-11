using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Sufni.App.Presentation;
using Sufni.App.ExtensionHost.Contracts.Presentation;

namespace Sufni.App.Views.Converters;

public sealed class SurfaceStateToGridLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is SurfacePresentationState { ReservesLayout: true })
        {
            return GridLengthParameterParser.Parse(parameter);
        }

        return new GridLength(0, GridUnitType.Pixel);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

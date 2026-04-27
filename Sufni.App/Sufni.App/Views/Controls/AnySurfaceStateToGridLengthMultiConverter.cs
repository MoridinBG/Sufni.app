using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Sufni.App.Presentation;

namespace Sufni.App.Views.Controls;

public sealed class AnySurfaceStateToGridLengthMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var value in values)
        {
            if (value is SurfacePresentationState { ReservesLayout: true })
            {
                return GridLength.Parse(parameter?.ToString() ?? "Auto");
            }

            if (value is bool boolValue && boolValue)
            {
                return GridLength.Parse(parameter?.ToString() ?? "Auto");
            }
        }

        return new GridLength(0, GridUnitType.Pixel);
    }
}
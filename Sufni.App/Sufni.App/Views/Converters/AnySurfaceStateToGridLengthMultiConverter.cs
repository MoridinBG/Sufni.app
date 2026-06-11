using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Sufni.App.Presentation;
using Sufni.App.ExtensionHost.Contracts.Presentation;

namespace Sufni.App.Views.Converters;

public sealed class AnySurfaceStateToGridLengthMultiConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        foreach (var value in values)
        {
            if (value is SurfacePresentationState { ReservesLayout: true })
            {
                return GridLengthParameterParser.Parse(parameter);
            }

            if (value is bool boolValue && boolValue)
            {
                return GridLengthParameterParser.Parse(parameter);
            }
        }

        return new GridLength(0, GridUnitType.Pixel);
    }
}

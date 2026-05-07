using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Sufni.App.Presentation;

namespace Sufni.App.Views.Controls;

public sealed class SurfaceStateToViewportFractionGridLengthConverter : IMultiValueConverter
{
    private const double DefaultRowsPerViewport = 3;
    private const double MinimumRowHeight = 180;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not SurfacePresentationState { ReservesLayout: true })
        {
            return new GridLength(0, GridUnitType.Pixel);
        }

        var viewportHeight = values[1] switch
        {
            double height when double.IsFinite(height) && height > 0 => height,
            _ => MinimumRowHeight * DefaultRowsPerViewport,
        };

        var rowsPerViewport = parameter is not null
            && double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRows)
            && double.IsFinite(parsedRows)
            && parsedRows > 0
                ? parsedRows
                : DefaultRowsPerViewport;

        return new GridLength(Math.Max(MinimumRowHeight, viewportHeight / rowsPerViewport), GridUnitType.Pixel);
    }
}

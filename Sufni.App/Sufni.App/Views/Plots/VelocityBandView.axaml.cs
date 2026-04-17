using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace Sufni.App.Views.Plots;

public class GridLengthConverter : IValueConverter
{
    public static readonly GridLengthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return new GridLength(0, GridUnitType.Star);
        }

        if (value is double length)
        {
            return new GridLength(length, GridUnitType.Star);
        }

        return new BindingNotification(new InvalidCastException(), BindingErrorType.Error);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class VelocityBandView : TemplatedControl
{
    public static readonly StyledProperty<double?> HsrPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "HsrPercentage");

    public double? HsrPercentage
    {
        get => GetValue(HsrPercentageProperty);
        set => SetValue(HsrPercentageProperty, value);
    }

    public static readonly StyledProperty<double?> LsrPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "LsrPercentage");

    public double? LsrPercentage
    {
        get => GetValue(LsrPercentageProperty);
        set => SetValue(LsrPercentageProperty, value);
    }

    public static readonly StyledProperty<double?> LscPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "LscPercentage");

    public double? LscPercentage
    {
        get => GetValue(LscPercentageProperty);
        set => SetValue(LscPercentageProperty, value);
    }

    public static readonly StyledProperty<double?> HscPercentageProperty = AvaloniaProperty.Register<VelocityBandView, double?>(
        "HscPercentage");

    public double? HscPercentage
    {
        get => GetValue(HscPercentageProperty);
        set => SetValue(HscPercentageProperty, value);
    }
}
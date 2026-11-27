using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Plots;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Views;

/// <summary>The theme-invariant terrain scale colour of a grade band, a roughness level or a label class.</summary>
public sealed class TerrainSwatchConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        GradeBand band => new SolidColorBrush(SufniThemes.SignalSeries.Terrain.ColorFor(band)),
        RoughnessClass level => new SolidColorBrush(SufniThemes.SignalSeries.Terrain.ColorFor(level)),
        TerrainLabelClass.Roughness roughness => new SolidColorBrush(SufniThemes.SignalSeries.Terrain.ColorFor(roughness.Class)),
        TerrainLabelClass.Steepness steepness => new SolidColorBrush(SufniThemes.SignalSeries.Terrain.ColorFor(steepness.Band)),
        _ => null
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

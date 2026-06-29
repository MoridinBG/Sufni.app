using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Svg.Skia;

namespace Sufni.App.Views.Converters;

public sealed class SvgImagePathConverter : IValueConverter
{
    private static readonly Uri SvgAssetBaseUri =
        new($"avares://{typeof(global::Sufni.App.App).Assembly.GetName().Name}/");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string path && !string.IsNullOrWhiteSpace(path)
            ? new SvgImage { Source = SvgSource.Load(path, SvgAssetBaseUri) }
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

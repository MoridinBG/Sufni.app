using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Sufni.App.Acquisition.Models;

namespace Sufni.App.Acquisition.Views.Converters;

public sealed class ImportActionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ImportFileAction.Import => 1,
            ImportFileAction.Trash => 2,
            _ => 0,
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            1 => ImportFileAction.Import,
            2 => ImportFileAction.Trash,
            _ => ImportFileAction.Ignore,
        };
}

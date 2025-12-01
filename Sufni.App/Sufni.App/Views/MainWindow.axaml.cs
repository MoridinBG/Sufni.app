using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;
using Sufni.App.ViewModels;

namespace Sufni.App.Views;

public class IsEqualConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        return values.Count == 2 && values[0] == values[1];
    }
}

public class BoolToFontStyleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? FontStyle.Italic : FontStyle.Normal;
        }
        return FontStyle.Normal;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class BoolToColorConverter : IValueConverter
{
    private static readonly Brush NormalBrush = new SolidColorBrush(Color.Parse("#a0a0a0"));
    private static readonly Brush DirtyBrush = new SolidColorBrush(Color.Parse("#daa520"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? DirtyBrush : NormalBrush;
        }
        return NormalBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void TabHeader_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var header = sender as Control;
        Debug.Assert(header is not null);

        var point = e.GetCurrentPoint(header);
        if (!point.Properties.IsMiddleButtonPressed) return;

        var vm = header.DataContext as TabPageViewModelBase;
        vm?.CloseCommand.Execute(null);
    }
}
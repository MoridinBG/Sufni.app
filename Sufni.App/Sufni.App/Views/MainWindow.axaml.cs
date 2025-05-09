using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Media;

namespace Sufni.App.Views;

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
            
#if DEBUG
            
        this.AttachDevTools(new KeyGesture(Key.F12, KeyModifiers.Alt));
            
#endif
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Sufni.App.Theming;
using Sufni.App.ViewModels;

namespace Sufni.App.DesktopViews;

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

public class BoolToColorConverter : IMultiValueConverter
{
    private const string NormalBrushKey = "SufniTabTextBrush";
    private const string DirtyBrushKey = "SufniStatusWarningBrush";

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDirty = values.Count > 0 && values[0] is bool b && b;
        var variant = values.Count > 1 && values[1] is ThemeVariant tv
            ? tv
            : Application.Current?.ActualThemeVariant;
        var key = isDirty ? DirtyBrushKey : NormalBrushKey;
        return ResolveBrush(key, variant) ?? ResolveFallbackBrush(isDirty, variant);
    }

    private static IBrush? ResolveBrush(string key, ThemeVariant? variant)
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        return app.TryFindResource(key, variant, out var resource) && resource is IBrush brush
            ? brush
            : null;
    }

    private static IBrush ResolveFallbackBrush(bool isDirty, ThemeVariant? variant)
    {
        var theme = SufniThemes.FromVariant(variant);
        if (isDirty && theme.Status.Warning is { } warning)
        {
            return warning.ToBrush();
        }

        return theme.Tab.Text.ToBrush();
    }
}

public static class TabStripMiddleClickHandler
{
    public static void Register()
    {
        InputElement.PointerPressedEvent.AddClassHandler(
            typeof(TabStripItem),
            OnPointerPressed,
            RoutingStrategies.Tunnel);
    }

    private static void OnPointerPressed(object? sender, RoutedEventArgs e)
    {
        var header = sender as Control;
        Debug.Assert(header is not null);

        var args = e as PointerPressedEventArgs;
        Debug.Assert(args is not null);

        var point = args.GetCurrentPoint(header);
        if (!point.Properties.IsMiddleButtonPressed) return;

        var vm = header.DataContext as TabPageViewModelBase;
        vm?.CloseCommand.Execute(null);
    }
}

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        TabStripMiddleClickHandler.Register();
    }
}

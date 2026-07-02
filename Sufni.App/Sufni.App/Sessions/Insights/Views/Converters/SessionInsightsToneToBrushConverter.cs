using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Insights.Views.Converters;

/// <summary>
/// Maps a <see cref="SessionInsightsStatusTone"/> to the theme brush used for
/// verdict pills, finding severity, and metric-status indicators. Neutral stays
/// quiet (secondary text); Watch/Action reuse the existing warning/danger
/// palette. Resolves live theme brushes with static fallbacks so headless view
/// tests render without the app resource dictionary loaded.
/// </summary>
public sealed class SessionInsightsToneToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            SessionInsightsStatusTone.Action => ResolveBrush("SufniDangerBrush", Colors.Firebrick),
            SessionInsightsStatusTone.Watch => ResolveBrush("SufniStatusWarningBrush", Colors.Goldenrod),
            _ => ResolveBrush("SufniTextSecondaryBrush", Colors.Gray),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static IBrush ResolveBrush(string key, Color fallback)
    {
        var app = Application.Current;
        if (app is not null && app.TryFindResource(key, app.ActualThemeVariant, out var resource))
        {
            return resource switch
            {
                IBrush brush => brush,
                Color color => new SolidColorBrush(color),
                _ => new SolidColorBrush(fallback),
            };
        }

        return new SolidColorBrush(fallback);
    }
}

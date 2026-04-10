using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.Behaviors;

// Aliased so the field type below stays unambiguous against the
// behavior class name.
using PlatformHaptics = global::Sufni.App.Services.IHapticFeedback;

public static class HapticFeedbackBehavior
{
    public static readonly RoutedEvent<RoutedEventArgs> LongPressFeedbackRequestedEvent =
        RoutedEvent.Register<Control, RoutedEventArgs>(
            "LongPressFeedbackRequested", RoutingStrategies.Bubble);

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsEnabled", typeof(HapticFeedbackBehavior));

    public static void SetIsEnabled(Control element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(Control element) =>
        element.GetValue(IsEnabledProperty);

    private static readonly PlatformHaptics? feedback =
        App.Current?.Services?.GetService<PlatformHaptics>();

    static HapticFeedbackBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<Control>(OnIsEnabledChanged);
    }

    private static void OnIsEnabledChanged(Control host, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            host.AddHandler(LongPressFeedbackRequestedEvent, OnLongPressRequested);
        }
        else
        {
            host.RemoveHandler(LongPressFeedbackRequestedEvent, OnLongPressRequested);
        }
    }

    private static void OnLongPressRequested(object? sender, RoutedEventArgs e) =>
        feedback?.LongPress();
}
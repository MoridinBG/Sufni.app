using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Sufni.App.Shell.Behaviors;

public static class HapticFeedbackBehavior
{
    public static readonly RoutedEvent<RoutedEventArgs> LongPressFeedbackRequestedEvent =
        RoutedEvent.Register<Control, RoutedEventArgs>(
            "LongPressFeedbackRequested", RoutingStrategies.Bubble);

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsEnabled", typeof(HapticFeedbackBehavior));

    public static readonly AttachedProperty<Action?> LongPressFeedbackProperty =
        AvaloniaProperty.RegisterAttached<StyledElement, Action?>(
            "LongPressFeedback",
            typeof(HapticFeedbackBehavior),
            defaultValue: null,
            inherits: true);

    public static void SetIsEnabled(Control element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    public static bool GetIsEnabled(Control element) =>
        element.GetValue(IsEnabledProperty);

    public static void SetLongPressFeedback(StyledElement element, Action? value) =>
        element.SetValue(LongPressFeedbackProperty, value);

    public static Action? GetLongPressFeedback(StyledElement element) =>
        element.GetValue(LongPressFeedbackProperty);

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

    private static void OnLongPressRequested(object? sender, RoutedEventArgs e)
    {
        if (sender is StyledElement host)
        {
            GetLongPressFeedback(host)?.Invoke();
        }
    }
}

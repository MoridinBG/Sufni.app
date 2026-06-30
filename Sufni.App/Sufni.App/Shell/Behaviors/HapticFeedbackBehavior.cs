using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.Shell.Behaviors;

// Aliased so the field type below stays unambiguous against the
// behavior class name.
using PlatformHaptics = global::Sufni.App.Infrastructure.IHapticFeedback;

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

    // Resolved at use rather than cached at type initialization: the static
    // field variant ran before the service provider was built and went stale
    // across test apps.
    private static PlatformHaptics? Feedback =>
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
        Feedback?.LongPress();
}
using System;
using Avalonia;
using Sufni.App.Infrastructure;

namespace Sufni.App.Shared.Views;

/// <summary>Supplies the root's timer scheduler to XAML-created descendants.</summary>
public static class UiTimers
{
    public static readonly AttachedProperty<IUiTimerScheduler?> SchedulerProperty =
        AvaloniaProperty.RegisterAttached<StyledElement, IUiTimerScheduler?>(
            "Scheduler", typeof(UiTimers), defaultValue: null, inherits: true);

    public static void SetScheduler(StyledElement element, IUiTimerScheduler scheduler) =>
        element.SetValue(SchedulerProperty, scheduler);

    public static IUiTimerScheduler GetScheduler(StyledElement element) =>
        element.GetValue(SchedulerProperty)
        ?? throw new InvalidOperationException("The view root must supply a UI timer scheduler.");
}

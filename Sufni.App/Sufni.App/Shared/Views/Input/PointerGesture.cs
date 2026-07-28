using System;
using Avalonia;
using Avalonia.Input;
using Sufni.App.Infrastructure;

namespace Sufni.App.Shared.Views.Input;

public static class PointerGesture
{
    public const int AnalysisLongPressDelayMilliseconds = 500;
    public const int DampingCutoffLongPressDelayMilliseconds = 250;

    public static readonly AttachedProperty<bool> SupportsTouchLongPressContextMenuProperty =
        AvaloniaProperty.RegisterAttached<StyledElement, bool>(
            "SupportsTouchLongPressContextMenu",
            typeof(PointerGesture),
            defaultValue: false,
            inherits: true);

    public static TimeSpan AnalysisLongPressDelay { get; } =
        TimeSpan.FromMilliseconds(AnalysisLongPressDelayMilliseconds);

    public static TimeSpan DampingCutoffLongPressDelay { get; } =
        TimeSpan.FromMilliseconds(DampingCutoffLongPressDelayMilliseconds);

    public static bool IsPrimaryPressed(PointerEventArgs args, Visual relativeTo)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(relativeTo);

        var point = args.GetCurrentPoint(relativeTo);
        return point.Properties.IsLeftButtonPressed || args.Pointer.Type != PointerType.Mouse;
    }

    public static bool IsSecondaryPressed(PointerEventArgs args, Visual relativeTo)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(relativeTo);

        var point = args.GetCurrentPoint(relativeTo);
        return point.Properties.IsRightButtonPressed;
    }

    public static void SetSupportsTouchLongPressContextMenu(StyledElement element, bool value) =>
        element.SetValue(SupportsTouchLongPressContextMenuProperty, value);

    public static bool GetSupportsTouchLongPressContextMenu(StyledElement element) =>
        element.GetValue(SupportsTouchLongPressContextMenuProperty);

    public static bool SupportsTouchLongPressContextMenu(StyledElement element)
    {
        return GetSupportsTouchLongPressContextMenu(element);
    }

    public static bool SupportsTouchLongPressContextMenu(InputCapabilities input)
    {
        return input.HasTouch && input.SupportsLongPressContextMenu;
    }
}

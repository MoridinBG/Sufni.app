using System;
using Avalonia;
using Avalonia.Input;
using Sufni.App.Infrastructure;

namespace Sufni.App.Shared.Views.Input;

public static class PointerGesture
{
    public const int AnalysisLongPressDelayMilliseconds = 500;
    public const int DampingCutoffLongPressDelayMilliseconds = 250;

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

    public static bool SupportsTouchLongPressContextMenu()
    {
        return SupportsTouchLongPressContextMenu(ResolveInputCapabilities());
    }

    public static bool SupportsTouchLongPressContextMenu(InputCapabilities input)
    {
        return input.HasTouch && input.SupportsLongPressContextMenu;
    }

    private static InputCapabilities ResolveInputCapabilities()
    {
        return App.Current?.Services?.GetService(typeof(IAppEnvironment)) is IAppEnvironment environment
            ? environment.Input
            : new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsPinch: false,
                SupportsLongPressContextMenu: false);
    }
}

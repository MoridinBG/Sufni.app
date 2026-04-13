using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Sufni.App.ViewModels.Editors;

public sealed partial class SessionTimelineLinkViewModel : ObservableObject
{
    private const double Epsilon = 0.000001;

    [ObservableProperty] private double? normalizedCursorPosition;
    [ObservableProperty] private double visibleRangeStart;
    [ObservableProperty] private double visibleRangeEnd = 1;

    public void SetCursorPosition(double? normalizedPosition)
    {
        double? clamped = normalizedPosition is null
            ? null
            : Math.Clamp(normalizedPosition.Value, 0, 1);

        if ((clamped, NormalizedCursorPosition) is (null, null))
        {
            return;
        }

        if (clamped is not null &&
            NormalizedCursorPosition is not null &&
            AreClose(clamped.Value, NormalizedCursorPosition.Value))
        {
            return;
        }

        NormalizedCursorPosition = clamped;
    }

    public void ClearCursorPosition()
    {
        SetCursorPosition(null);
    }

    public void SetVisibleRange(double start, double end)
    {
        if (double.IsNaN(start) || double.IsNaN(end) || double.IsInfinity(start) || double.IsInfinity(end))
        {
            return;
        }

        start = Math.Clamp(start, 0, 1);
        end = Math.Clamp(end, 0, 1);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        if (AreClose(start, VisibleRangeStart) && AreClose(end, VisibleRangeEnd))
        {
            return;
        }

        VisibleRangeStart = start;
        VisibleRangeEnd = end;
    }

    private static bool AreClose(double left, double right)
    {
        return Math.Abs(left - right) <= Epsilon;
    }
}
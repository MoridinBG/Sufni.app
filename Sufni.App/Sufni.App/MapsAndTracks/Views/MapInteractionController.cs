using System;

namespace Sufni.App.MapsAndTracks.Views;

/// <summary>
/// Gesture/interaction state for <see cref="MapView"/>: tracks whether the
/// pointer is driving the viewport, coalesces viewport-changed notifications
/// through an injected post seam, and gates them while a timeline update is
/// being applied (so Mapsui's own viewport events do not echo back as
/// user-driven).
/// </summary>
internal sealed class MapInteractionController(Action<Action> postNotification)
{
    private bool pointerInteractionActive;
    private bool notificationQueued;
    private bool applyingTimelineUpdate;

    public event Action? ViewportNotificationDue;

    public bool IsPointerInteractionActive => pointerInteractionActive;

    public bool IsApplyingTimelineUpdate => applyingTimelineUpdate;

    public void PointerPressed() => pointerInteractionActive = true;

    public void PointerMoved()
    {
        if (pointerInteractionActive)
        {
            QueueNotification();
        }
    }

    public void PointerReleasedOrCaptureLost()
    {
        QueueNotification();
        pointerInteractionActive = false;
    }

    public void WheelChanged() => QueueNotification();

    public void NavigatorViewportChanged()
    {
        if (pointerInteractionActive)
        {
            QueueNotification();
        }
    }

    public void RunWithoutViewportTimelineUpdates(Action action)
    {
        if (applyingTimelineUpdate)
        {
            action();
            return;
        }

        applyingTimelineUpdate = true;
        try
        {
            action();
        }
        finally
        {
            applyingTimelineUpdate = false;
        }
    }

    private void QueueNotification()
    {
        if (notificationQueued || applyingTimelineUpdate)
        {
            return;
        }

        notificationQueued = true;
        postNotification(() =>
        {
            notificationQueued = false;
            ViewportNotificationDue?.Invoke();
        });
    }
}

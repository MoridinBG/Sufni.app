using System;
using Avalonia.Threading;

namespace Sufni.App.Shared.Common;

internal static class PeriodicUiTimer
{
    public static IDisposable SchedulePeriodic(TimeSpan interval, Action action)
    {
        var timer = CreateTimer(interval);
        timer.Tick += OnTick;
        timer.Start();
        return new DispatcherTimerSubscription(timer, OnTick);

        void OnTick(object? sender, EventArgs args)
        {
            action();
        }
    }

    public static IDisposable ScheduleOnce(TimeSpan interval, Action action)
    {
        var timer = CreateTimer(interval);
        timer.Tick += OnTick;
        timer.Start();
        return new DispatcherTimerSubscription(timer, OnTick);

        void OnTick(object? sender, EventArgs args)
        {
            timer.Tick -= OnTick;
            timer.Stop();
            action();
        }
    }

    private static DispatcherTimer CreateTimer(TimeSpan interval) => new(DispatcherPriority.Normal)
    {
        Interval = interval
    };

    private sealed class DispatcherTimerSubscription(
        DispatcherTimer timer,
        EventHandler tickHandler) : IDisposable
    {
        public void Dispose()
        {
            timer.Tick -= tickHandler;
            timer.Stop();
        }
    }
}

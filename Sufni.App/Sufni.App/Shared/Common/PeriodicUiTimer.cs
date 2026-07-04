using System;
using System.Threading;
using Avalonia.Threading;

namespace Sufni.App.Shared.Common;

internal static class PeriodicUiTimer
{
    private static IPeriodicUiTimerScheduler scheduler = DispatcherPeriodicUiTimerScheduler.Instance;

    public static IDisposable SchedulePeriodic(TimeSpan interval, Action action)
    {
        return scheduler.SchedulePeriodic(interval, action);
    }

    public static IDisposable ScheduleOnce(TimeSpan interval, Action action)
    {
        return scheduler.ScheduleOnce(interval, action);
    }

    internal static IDisposable UseSchedulerForTests(IPeriodicUiTimerScheduler testScheduler)
    {
        ArgumentNullException.ThrowIfNull(testScheduler);
        var previous = Interlocked.Exchange(ref scheduler, testScheduler);
        return new SchedulerOverride(previous);
    }

    internal interface IPeriodicUiTimerScheduler
    {
        IDisposable SchedulePeriodic(TimeSpan interval, Action action);

        IDisposable ScheduleOnce(TimeSpan interval, Action action);
    }

    private sealed class DispatcherPeriodicUiTimerScheduler : IPeriodicUiTimerScheduler
    {
        public static readonly DispatcherPeriodicUiTimerScheduler Instance = new();

        public IDisposable SchedulePeriodic(TimeSpan interval, Action action)
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

        public IDisposable ScheduleOnce(TimeSpan interval, Action action)
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
    }

    private sealed class SchedulerOverride(IPeriodicUiTimerScheduler previous) : IDisposable
    {
        private int isDisposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref isDisposed, 1) != 0)
            {
                return;
            }

            Interlocked.Exchange(ref scheduler, previous);
        }
    }

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

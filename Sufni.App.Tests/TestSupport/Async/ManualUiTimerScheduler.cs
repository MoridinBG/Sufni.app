using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.TestSupport.Async;

public sealed class ManualUiTimerScheduler :
    IUiTimerScheduler,
    IDisposable
{
    private readonly object gate = new();
    private readonly List<ScheduledTimer> timers = [];
    private bool isDisposed;

    public bool HasScheduledTimers
    {
        get
        {
            lock (gate)
            {
                return timers.Count > 0;
            }
        }
    }

    public IDisposable SchedulePeriodic(TimeSpan interval, Action action)
    {
        return AddTimer(repeats: true, action);
    }

    public IDisposable ScheduleOnce(TimeSpan interval, Action action)
    {
        return AddTimer(repeats: false, action);
    }

    public void FireAll()
    {
        ScheduledTimer[] snapshot;
        lock (gate)
        {
            snapshot = [.. timers];
        }

        if (snapshot.Length == 0)
        {
            throw new InvalidOperationException("No periodic UI timers are scheduled.");
        }

        foreach (var timer in snapshot)
        {
            timer.Fire();
        }
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        ScheduledTimer[] snapshot;
        lock (gate)
        {
            snapshot = [.. timers];
            timers.Clear();
        }

        foreach (var timer in snapshot)
        {
            timer.Dispose();
        }
    }

    private IDisposable AddTimer(bool repeats, Action action)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(action);

        var timer = new ScheduledTimer(this, repeats, action);
        lock (gate)
        {
            timers.Add(timer);
        }

        return timer;
    }

    private void Remove(ScheduledTimer timer)
    {
        lock (gate)
        {
            timers.Remove(timer);
        }
    }

    private sealed class ScheduledTimer(
        ManualUiTimerScheduler owner,
        bool repeats,
        Action action) : IDisposable
    {
        private bool isDisposed;

        public void Fire()
        {
            if (isDisposed)
            {
                return;
            }

            if (!repeats)
            {
                Dispose();
            }

            action();
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            owner.Remove(this);
        }
    }
}

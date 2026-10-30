using System;

namespace Sufni.App.Infrastructure;

/// <summary>Schedules cancellable callbacks on the UI thread.</summary>
public interface IUiTimerScheduler
{
    IDisposable SchedulePeriodic(TimeSpan interval, Action action);
    IDisposable ScheduleOnce(TimeSpan interval, Action action);
}

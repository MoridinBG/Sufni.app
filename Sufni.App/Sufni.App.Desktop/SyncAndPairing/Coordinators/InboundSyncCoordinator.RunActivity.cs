using System;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.SyncAndPairing.Coordinators;

public sealed partial class InboundSyncCoordinator
{
    private static readonly TimeSpan RunSilenceTimeout = TimeSpan.FromMinutes(2);
    private readonly BehaviorSubject<PeerSyncActivity> activity = new(new PeerSyncActivity.Idle());
    private SynchronizationPhase[] runPhases = [];
    private ITimer? interruptionTimer;
    private bool disposed;

    public IObservable<PeerSyncActivity> Activity => activity.AsObservable();

    public async Task<SynchronizationRunStarted> BeginRunAsync(string deviceId, SynchronizationRunStart request)
    {
        var incarnation = await backgroundTaskRunner.RunAsync(syncDataStore.GetDatabaseIncarnationAsync);
        if (incarnation == request.DatabaseIncarnation)
        {
            await uiThreadDispatcher.InvokeAsync(() =>
            {
                runPhases = request.Phases.ToArray();
                activity.OnNext(new PeerSyncActivity.Running(deviceId, request.RunId,
                    SynchronizationProgressSnapshot.ForPhase(runPhases, runPhases[0]), clock.GetUtcNow()));
                ResetInterruptionTimer();
            });
        }
        return new SynchronizationRunStarted(incarnation);
    }

    public Task ObserveRequestAsync(string deviceId, Guid runId, SynchronizationPhase phase, SynchronizationItemProgress? items = null) =>
        uiThreadDispatcher.InvokeAsync(() =>
        {
            if (activity.Value is not PeerSyncActivity.Running current ||
                current.DeviceId != deviceId || current.RunId != runId) return;
            var step = Array.IndexOf(runPhases, phase);
            if (step < Array.IndexOf(runPhases, current.Progress.Phase)) return;
            if (items is not null && !items.IsValidFor(phase)) return;
            if (current.Progress.Items is { } previous && items is not null &&
                (items.RunTotal != previous.RunTotal ||
                 (phase == current.Progress.Phase
                     ? items.Previous != previous.Previous || items.Total != previous.Total || items.Checked < previous.Checked
                     : items.Previous < previous.Previous + previous.Total))) return;

            var progress = phase == current.Progress.Phase && items is null
                ? current.Progress
                : SynchronizationProgressSnapshot.ForPhase(runPhases, phase, items);
            activity.OnNext(current with { Progress = progress, LastRequest = clock.GetUtcNow() });
            ResetInterruptionTimer();
        });

    public Task CompleteRunAsync(string deviceId, Guid runId) => uiThreadDispatcher.InvokeAsync(() =>
    {
        if (activity.Value is not PeerSyncActivity.Running current ||
            current.DeviceId != deviceId || current.RunId != runId) return;
        interruptionTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        activity.OnNext(new PeerSyncActivity.Completed(runId));
    });

    private void ResetInterruptionTimer()
    {
        interruptionTimer ??= clock.CreateTimer(_ => uiThreadDispatcher.Post(InterruptSilentRun),
            null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        interruptionTimer.Change(RunSilenceTimeout, Timeout.InfiniteTimeSpan);
    }

    private void InterruptSilentRun()
    {
        if (!disposed && activity.Value is PeerSyncActivity.Running current &&
            clock.GetUtcNow() - current.LastRequest >= RunSilenceTimeout)
            activity.OnNext(new PeerSyncActivity.Interrupted(current.RunId));
    }
}

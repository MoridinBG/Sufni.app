using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Shared.Models;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Setups.Coordinators;
using Sufni.App.SyncAndPairing.Coordinators;

namespace Sufni.App.Shell.Coordinators;

public abstract record PendingDeleteKey
{
    private PendingDeleteKey() { }
    public sealed record Bike(Guid Id) : PendingDeleteKey;
    public sealed record Setup(Guid Id) : PendingDeleteKey;
    public sealed record Session(Guid Id) : PendingDeleteKey;
    public sealed record PairedDevice(string Id) : PendingDeleteKey;
}

public sealed record PendingDelete(PendingDeleteKey Key, string Name);
public sealed record PendingDeleteCompletion(PendingDeleteKey Key, FailurePresentation? Error);

public interface IPendingDeletes
{
    IObservable<IReadOnlyList<PendingDelete>> Observe();
    event EventHandler<PendingDeleteCompletion>? Completed;
    bool IsPending(PendingDeleteKey key);
    void Schedule(PendingDeleteKey key, string name);
    void Undo(PendingDeleteKey key);
    Task CommitAsync(PendingDeleteKey key);
    Task FinalizeAllAsync();
}

// In-memory undo windows belong to the application, including while their lists are detached.
public sealed class PendingDeletes(
    IBikeCoordinator bikes,
    ISetupCoordinator setups,
    ISessionCoordinator sessions,
    IPairedDeviceCoordinator pairedDevices,
    IShellCoordinator shell,
    IUiThreadDispatcher dispatcher,
    IBackgroundTaskRunner backgroundTaskRunner,
    TimeProvider timeProvider) : IPendingDeletes, IDisposable
{
    private static readonly ILogger logger = Log.ForContext<PendingDeletes>();
    private readonly Dictionary<PendingDeleteKey, Entry> entries = [];
    private readonly BehaviorSubject<IReadOnlyList<PendingDelete>> pending = new(Array.Empty<PendingDelete>());

    public IObservable<IReadOnlyList<PendingDelete>> Observe() => pending.AsObservable();
    public event EventHandler<PendingDeleteCompletion>? Completed;
    public bool IsPending(PendingDeleteKey key) => entries.ContainsKey(key);

    public void Schedule(PendingDeleteKey key, string name)
    {
        if (entries.ContainsKey(key)) return;
        var entry = new Entry(new PendingDelete(key, name));
        entries.Add(key, entry);
        Publish();
        entry.Expiry = ExpireAsync(entry);
    }

    public void Undo(PendingDeleteKey key)
    {
        if (!entries.TryGetValue(key, out var entry) || entry.Commit is not null) return;
        entries.Remove(key);
        entry.Cancellation.Cancel();
        Publish();
    }

    public async Task CommitAsync(PendingDeleteKey key)
    {
        var task = await dispatcher.InvokeAsync<Task>(() => BeginCommit(key));
        await task;
    }

    public async Task FinalizeAllAsync()
    {
        var tasks = await dispatcher.InvokeAsync(() => entries.Values.ToArray().SelectMany(entry => new[] { BeginCommit(entry.Value.Key), entry.Expiry }).ToArray());
        await Task.WhenAll(tasks);
    }

    private async Task ExpireAsync(Entry entry)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), timeProvider, entry.Cancellation.Token);
            await CommitAsync(entry.Value.Key);
        }
        catch (OperationCanceledException) when (entry.Cancellation.IsCancellationRequested) { }
        finally { entry.Cancellation.Dispose(); }
    }

    private Task BeginCommit(PendingDeleteKey key)
    {
        if (!entries.TryGetValue(key, out var entry)) return Task.CompletedTask;
        if (entry.Commit is not null) return entry.Commit;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        entry.Commit = completion.Task;
        entry.Cancellation.Cancel();
        Publish();
        _ = CompleteAsync(entry, completion);
        return completion.Task;
    }

    private async Task CompleteAsync(Entry entry, TaskCompletionSource completion)
    {
        FailurePresentation? error = null;
        try
        {
            var result = await backgroundTaskRunner.RunAsync(() => DeleteAsync(entry.Value.Key));
            error = result.Error;
            if (result.Editor is { } editor)
                await dispatcher.InvokeAsync(() => shell.CloseEditorAsync(editor));
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Pending delete failed for {Key}", entry.Value.Key);
            error = FailurePresentation.From(PendingDeleteFailureReason.Delete, exception.Message);
        }

        await dispatcher.InvokeAsync(() =>
        {
            entries.Remove(entry.Value.Key);
            Publish();
            Completed?.Invoke(this, new PendingDeleteCompletion(entry.Value.Key, error));
        });
        completion.SetResult();
    }

    private async Task<(FailurePresentation? Error, EditorKey? Editor)> DeleteAsync(PendingDeleteKey key)
    {
        switch (key)
        {
            case PendingDeleteKey.Bike bike:
                var bikeResult = await bikes.DeleteAsync(bike.Id);
                return bikeResult switch
                {
                    BikeDeleteResult.Deleted => (null, new EditorKey.Bike(bike.Id)),
                    BikeDeleteResult.InUse => (new FailurePresentation("Bike is referenced by a setup and cannot be deleted."), null),
                    BikeDeleteResult.Failed bikeFailure => (FailurePresentation.From(bikeFailure.Reason, bikeFailure.Details), null),
                    _ => throw new ArgumentOutOfRangeException(nameof(bikeResult))
                };
            case PendingDeleteKey.Setup setup:
                var setupResult = await setups.DeleteAsync(setup.Id);
                return setupResult switch
                {
                    SetupDeleteResult.InUse => (new FailurePresentation("Setup is referenced by a session and cannot be deleted."), null),
                    SetupDeleteResult.Deleted => (null, new EditorKey.Setup(setup.Id)),
                    SetupDeleteResult.Failed setupFailure => (FailurePresentation.From(setupFailure.Reason, setupFailure.Details), null),
                    _ => throw new ArgumentOutOfRangeException(nameof(setupResult))
                };
            case PendingDeleteKey.Session session:
                var sessionResult = await sessions.DeleteAsync(session.Id);
                return sessionResult switch
                {
                    SessionDeleteResult.Deleted => (null, new EditorKey.Session(session.Id)),
                    SessionDeleteResult.Failed failure => (FailurePresentation.From(failure.Reason, failure.Details), null),
                    _ => throw new ArgumentOutOfRangeException(nameof(sessionResult))
                };
            case PendingDeleteKey.PairedDevice device:
                var result = await pairedDevices.UnpairAsync(device.Id);
                return (result is PairedDeviceUnpairResult.Failed failed ? FailurePresentation.From(failed.Reason, failed.Details) : null, null);
            default:
                throw new ArgumentOutOfRangeException(nameof(key));
        }
    }

    private void Publish() => pending.OnNext(entries.Values.Where(e => e.Commit is null).Select(e => e.Value).ToArray());

    public void Dispose()
    {
        foreach (var entry in entries.Values.Where(e => e.Commit is null)) entry.Cancellation.Cancel();
        pending.Dispose();
    }

    private sealed class Entry(PendingDelete value)
    {
        public PendingDelete Value { get; } = value;
        public CancellationTokenSource Cancellation { get; } = new();
        public Task Expiry { get; set; } = Task.CompletedTask;
        public Task? Commit { get; set; }
    }
}

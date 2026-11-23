using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sufni.App.Shared.Common;

/// <summary>Tracks operations until completion and drains the work retained at the time of a call.</summary>
public sealed class RetainedWork
{
    private readonly Lock gate = new();
    private readonly HashSet<Task> tasks = [];

    public TTask Retain<TTask>(TTask task) where TTask : Task
    {
        lock (gate) tasks.Add(task);
        _ = ReleaseAsync(task);
        return task;
    }

    private async Task ReleaseAsync(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        lock (gate) tasks.Remove(task);
    }

    public async Task DrainAsync()
    {
        Task[] pending;
        lock (gate) pending = tasks.ToArray();
        await Task.WhenAll(pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
}

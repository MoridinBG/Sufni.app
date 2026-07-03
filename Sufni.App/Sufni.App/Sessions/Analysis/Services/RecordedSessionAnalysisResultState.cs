using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.Services;

public interface IRecordedSessionAnalysisResultState : IDisposable
{
    RecordedSessionAnalysisInputs? CurrentInputs { get; }
    IObservable<RecordedSessionAnalysisInputs> ConnectInputs();
    IObservable<RecordedSessionAnalysisResultChanged> Connect();
    RecordedSessionAnalysisResult? Get(RecordedSessionAnalysisKey key);
    Task RequestAsync(RecordedSessionAnalysisKey key, CancellationToken cancellationToken = default);
    void Invalidate(RecordedSessionAnalysisInputs inputs);
}

internal interface IRecordedSessionAnalysisResultStateFactory
{
    IRecordedSessionAnalysisResultState Create(Func<TelemetryData?> telemetryAccessor);
}

internal sealed class RecordedSessionAnalysisResultStateFactory(
    IRecordedSessionAnalysisComputer computer,
    IBackgroundTaskRunner backgroundTaskRunner,
    IUiThreadDispatcher uiThreadDispatcher) : IRecordedSessionAnalysisResultStateFactory
{
    public IRecordedSessionAnalysisResultState Create(Func<TelemetryData?> telemetryAccessor) =>
        new RecordedSessionAnalysisResultState(
            computer,
            backgroundTaskRunner,
            uiThreadDispatcher,
            telemetryAccessor);
}

internal sealed class RecordedSessionAnalysisResultState(
    IRecordedSessionAnalysisComputer computer,
    IBackgroundTaskRunner backgroundTaskRunner,
    IUiThreadDispatcher uiThreadDispatcher,
    Func<TelemetryData?> telemetryAccessor) : IRecordedSessionAnalysisResultState
{
    private readonly object gate = new();
    private readonly Dictionary<RecordedSessionAnalysisKey, RecordedSessionAnalysisResult> results = [];
    private readonly Dictionary<RecordedSessionAnalysisKey, InFlightAnalysis> inFlight = [];
    private readonly Subject<RecordedSessionAnalysisInputs> inputChanges = new();
    private readonly Subject<RecordedSessionAnalysisResultChanged> changes = new();
    private RecordedSessionAnalysisInputs? currentInputs;
    private bool disposed;

    public RecordedSessionAnalysisInputs? CurrentInputs
    {
        get
        {
            lock (gate)
            {
                return disposed ? null : currentInputs;
            }
        }
    }

    public IObservable<RecordedSessionAnalysisResultChanged> Connect()
    {
        lock (gate)
        {
            return disposed ? Observable.Empty<RecordedSessionAnalysisResultChanged>() : changes.AsObservable();
        }
    }

    public IObservable<RecordedSessionAnalysisInputs> ConnectInputs()
    {
        lock (gate)
        {
            return disposed ? Observable.Empty<RecordedSessionAnalysisInputs>() : inputChanges.AsObservable();
        }
    }

    public RecordedSessionAnalysisResult? Get(RecordedSessionAnalysisKey key)
    {
        lock (gate)
        {
            if (disposed)
            {
                return null;
            }

            return results.GetValueOrDefault(key);
        }
    }

    public void Invalidate(RecordedSessionAnalysisInputs inputs)
    {
        var publishInputsChanged = false;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (inputs == currentInputs)
            {
                return;
            }

            currentInputs = inputs;

            foreach (var key in results.Keys.Where(key => !key.Matches(inputs)).ToArray())
            {
                results.Remove(key);
            }

            foreach (var (key, work) in inFlight.ToArray())
            {
                if (!key.Matches(inputs))
                {
                    inFlight.Remove(key);
                    work.StaleCancellation.Cancel();
                    work.StaleCancellation.Dispose();
                }
            }

            publishInputsChanged = true;
        }

        if (publishInputsChanged)
        {
            _ = PublishInputsChangedAsync(inputs);
        }
    }

    public Task RequestAsync(RecordedSessionAnalysisKey key, CancellationToken cancellationToken = default)
    {
        Task task;
        RecordedSessionAnalysisResultChanged? immediateChange = null;
        lock (gate)
        {
            if (disposed)
            {
                return Task.CompletedTask;
            }

            if (currentInputs is null || !key.Matches(currentInputs))
            {
                return Task.CompletedTask;
            }

            if (results.TryGetValue(key, out var cached))
            {
                immediateChange = new RecordedSessionAnalysisResultChanged(key, cached);
                task = Task.CompletedTask;
            }
            else if (inFlight.TryGetValue(key, out var existing))
            {
                task = existing.Task;
            }
            else if (telemetryAccessor() is not { } telemetry)
            {
                immediateChange = new RecordedSessionAnalysisResultChanged(key, Result: null);
                task = Task.CompletedTask;
            }
            else
            {
                var staleCancellation = new CancellationTokenSource();
                task = ComputeAndPublishAsync(key, telemetry, staleCancellation.Token);
                inFlight[key] = new InFlightAnalysis(task, staleCancellation);
                _ = task.ContinueWith(
                    _ => RemoveInFlight(key, task),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        if (immediateChange is not null)
        {
            return AwaitWithCallerCancellationAsync(PublishAsync(immediateChange), cancellationToken);
        }

        return AwaitWithCallerCancellationAsync(task, cancellationToken);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            currentInputs = null;
            foreach (var work in inFlight.Values)
            {
                work.StaleCancellation.Cancel();
                work.StaleCancellation.Dispose();
            }

            results.Clear();
            inFlight.Clear();
        }

        inputChanges.Dispose();
        changes.Dispose();
    }

    private async Task ComputeAndPublishAsync(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry,
        CancellationToken staleCancellationToken)
    {
        try
        {
            var result = await backgroundTaskRunner.RunAsync(
                () =>
                {
                    staleCancellationToken.ThrowIfCancellationRequested();
                    return computer.Compute(key, telemetry);
                },
                staleCancellationToken);
            staleCancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                if (disposed || currentInputs is null || !key.Matches(currentInputs))
                {
                    return;
                }

                results[key] = result;
            }

            await PublishAsync(new RecordedSessionAnalysisResultChanged(key, result));
        }
        catch (OperationCanceledException) when (staleCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                if (disposed || currentInputs is null || !key.Matches(currentInputs))
                {
                    return;
                }
            }

            await PublishAsync(new RecordedSessionAnalysisResultChanged(key, Result: null, exception));
        }
    }

    private void RemoveInFlight(RecordedSessionAnalysisKey key, Task task)
    {
        lock (gate)
        {
            if (inFlight.TryGetValue(key, out var current) && ReferenceEquals(current.Task, task))
            {
                inFlight.Remove(key);
                current.StaleCancellation.Dispose();
            }
        }
    }

    private static Task AwaitWithCallerCancellationAsync(Task task, CancellationToken cancellationToken)
    {
        return !cancellationToken.CanBeCanceled || task.IsCompleted
            ? task
            : task.WaitAsync(cancellationToken);
    }

    private Task PublishAsync(RecordedSessionAnalysisResultChanged change)
    {
        return uiThreadDispatcher.InvokeAsync(() =>
        {
            lock (gate)
            {
                if (disposed)
                {
                    return;
                }

                changes.OnNext(change);
            }
        });
    }

    private Task PublishInputsChangedAsync(RecordedSessionAnalysisInputs inputs)
    {
        return uiThreadDispatcher.InvokeAsync(() =>
        {
            lock (gate)
            {
                if (disposed || currentInputs != inputs)
                {
                    return;
                }

                inputChanges.OnNext(inputs);
            }
        });
    }

    private sealed record InFlightAnalysis(Task Task, CancellationTokenSource StaleCancellation);
}

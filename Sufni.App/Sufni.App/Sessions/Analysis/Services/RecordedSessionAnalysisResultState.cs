using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.Services;

public interface IRecordedSessionAnalysisResultState : IDisposable
{
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
    private readonly Dictionary<RecordedSessionAnalysisKey, Task> inFlight = [];
    private readonly Subject<RecordedSessionAnalysisResultChanged> changes = new();
    private RecordedSessionAnalysisInputs? currentInputs;
    private CancellationTokenSource staleWorkCancellation = new();
    private long version;
    private bool disposed;

    public IObservable<RecordedSessionAnalysisResultChanged> Connect() => changes.AsObservable();

    public RecordedSessionAnalysisResult? Get(RecordedSessionAnalysisKey key)
    {
        lock (gate)
        {
            return results.GetValueOrDefault(key);
        }
    }

    public void Invalidate(RecordedSessionAnalysisInputs inputs)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (inputs == currentInputs)
            {
                return;
            }

            currentInputs = inputs;
            version++;
            staleWorkCancellation.Cancel();
            staleWorkCancellation.Dispose();
            staleWorkCancellation = new CancellationTokenSource();

            results.Clear();
        }
    }

    public Task RequestAsync(RecordedSessionAnalysisKey key, CancellationToken cancellationToken = default)
    {
        Task task;
        RecordedSessionAnalysisResultChanged? immediateChange = null;
        lock (gate)
        {
            ThrowIfDisposed();
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
                return existing;
            }
            else if (telemetryAccessor() is not { } telemetry)
            {
                immediateChange = new RecordedSessionAnalysisResultChanged(key, Result: null);
                task = Task.CompletedTask;
            }
            else
            {
                var capturedVersion = version;
                var staleToken = staleWorkCancellation.Token;
                task = ComputeAndPublishAsync(key, telemetry, capturedVersion, staleToken, cancellationToken);
                inFlight[key] = task;
                _ = task.ContinueWith(
                    _ => RemoveInFlight(key, task),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        if (immediateChange is not null)
        {
            return PublishAsync(immediateChange);
        }

        return task;
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
            staleWorkCancellation.Cancel();
            staleWorkCancellation.Dispose();
            results.Clear();
            inFlight.Clear();
        }

        changes.Dispose();
    }

    private async Task ComputeAndPublishAsync(
        RecordedSessionAnalysisKey key,
        TelemetryData telemetry,
        long capturedVersion,
        CancellationToken staleCancellationToken,
        CancellationToken requestCancellationToken)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            staleCancellationToken,
            requestCancellationToken);
        var cancellationToken = linkedCancellation.Token;
        try
        {
            var result = await backgroundTaskRunner.RunAsync(
                () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return computer.Compute(key, telemetry);
                },
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            lock (gate)
            {
                if (disposed || capturedVersion != version || currentInputs is null || !key.Matches(currentInputs))
                {
                    return;
                }

                results[key] = result;
            }

            await PublishAsync(new RecordedSessionAnalysisResultChanged(key, result));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (gate)
            {
                if (disposed || capturedVersion != version || currentInputs is null || !key.Matches(currentInputs))
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
            if (inFlight.TryGetValue(key, out var current) && ReferenceEquals(current, task))
            {
                inFlight.Remove(key);
            }
        }
    }

    private Task PublishAsync(RecordedSessionAnalysisResultChanged change)
    {
        lock (gate)
        {
            if (disposed)
            {
                return Task.CompletedTask;
            }
        }

        return uiThreadDispatcher.InvokeAsync(() => changes.OnNext(change));
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(RecordedSessionAnalysisResultState));
        }
    }
}

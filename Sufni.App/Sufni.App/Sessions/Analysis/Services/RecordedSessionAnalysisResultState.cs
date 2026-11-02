using Serilog;
using Sufni.App.Shared.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Analysis.Services;

public interface ISessionAnalysisResultSource : IDisposable
{
    RecordedSessionAnalysisInputs? CurrentInputs { get; }
    IObservable<RecordedSessionAnalysisInputs?> ConnectInputs();
    IObservable<RecordedSessionAnalysisResultChanged> Connect();
    RecordedSessionAnalysisResult? Get(RecordedSessionAnalysisKey key);
}

public interface IRecordedSessionAnalysisResultState : ISessionAnalysisResultSource, IAsyncDisposable
{
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

/// <summary>UI-thread-owned analysis cache and requests; workers publish through the dispatcher.</summary>
internal sealed class RecordedSessionAnalysisResultState(
    IRecordedSessionAnalysisComputer computer,
    IBackgroundTaskRunner backgroundTaskRunner,
    IUiThreadDispatcher uiThreadDispatcher,
    Func<TelemetryData?> telemetryAccessor) : IRecordedSessionAnalysisResultState
{
    private static readonly ILogger logger = Log.ForContext<RecordedSessionAnalysisResultState>();
    private readonly Dictionary<RecordedSessionAnalysisKey, RecordedSessionAnalysisResult> results = [];
    private readonly Dictionary<RecordedSessionAnalysisKey, InFlightAnalysis> inFlight = [];
    private readonly HashSet<InFlightAnalysis> active = [];
    private readonly Subject<RecordedSessionAnalysisInputs> inputChanges = new();
    private readonly Subject<RecordedSessionAnalysisResultChanged> changes = new();
    private bool disposed;

    public RecordedSessionAnalysisInputs? CurrentInputs { get; private set; }

    public IObservable<RecordedSessionAnalysisResultChanged> Connect() =>
        disposed ? Observable.Empty<RecordedSessionAnalysisResultChanged>() : changes.AsObservable();

    public IObservable<RecordedSessionAnalysisInputs> ConnectInputs() =>
        disposed ? Observable.Empty<RecordedSessionAnalysisInputs>() : inputChanges.AsObservable();

    public RecordedSessionAnalysisResult? Get(RecordedSessionAnalysisKey key) => results.GetValueOrDefault(key);

    public void Invalidate(RecordedSessionAnalysisInputs inputs)
    {
        if (disposed || inputs == CurrentInputs) return;
        CurrentInputs = inputs;
        foreach (var key in results.Keys.Where(key => !key.Matches(inputs)).ToArray()) results.Remove(key);
        foreach (var (key, work) in inFlight.ToArray())
        {
            if (key.Matches(inputs)) continue;
            inFlight.Remove(key);
            work.Cancellation.Cancel();
        }
        inputChanges.OnNext(inputs);
    }

    public Task RequestAsync(RecordedSessionAnalysisKey key, CancellationToken cancellationToken = default)
    {
        if (disposed || CurrentInputs is null || !key.Matches(CurrentInputs)) return Task.CompletedTask;
        if (results.TryGetValue(key, out var cached))
        {
            changes.OnNext(new RecordedSessionAnalysisResultChanged(key, cached));
            return Task.CompletedTask;
        }
        if (!inFlight.TryGetValue(key, out var work))
        {
            if (telemetryAccessor() is not { } telemetry)
            {
                changes.OnNext(new RecordedSessionAnalysisResultChanged(key, null));
                return Task.CompletedTask;
            }
            work = new InFlightAnalysis();
            inFlight.Add(key, work);
            active.Add(work);
            _ = ComputeAndPublishAsync(key, telemetry, work);
        }
        return cancellationToken.CanBeCanceled && !work.Completion.Task.IsCompleted
            ? work.Completion.Task.WaitAsync(cancellationToken)
            : work.Completion.Task;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        CurrentInputs = null;
        foreach (var work in active.ToArray()) work.Cancellation.Cancel();
        results.Clear();
        inFlight.Clear();
        inputChanges.Dispose();
        changes.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.WhenAll(active.Select(work => work.Completion.Task));
    }

    private async Task ComputeAndPublishAsync(RecordedSessionAnalysisKey key, TelemetryData telemetry, InFlightAnalysis work)
    {
        RecordedSessionAnalysisResultChanged? change = null;
        try
        {
            var result = await backgroundTaskRunner.RunAsync(() =>
            {
                work.Cancellation.Token.ThrowIfCancellationRequested();
                return computer.Compute(key, telemetry);
            }, work.Cancellation.Token).ConfigureAwait(false);
            work.Cancellation.Token.ThrowIfCancellationRequested();
            change = new RecordedSessionAnalysisResultChanged(key, result);
        }
        catch (OperationCanceledException) when (work.Cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!work.Cancellation.IsCancellationRequested)
            {
                logger.Error(exception, "Recorded analysis {Family} failed for {Source}", key.Family, telemetry.Metadata.SourceName);
                change = new RecordedSessionAnalysisResultChanged(key, null,
                    new RecordedSessionAnalysisFailure(SessionEditorFailureReason.Analysis, exception.Message));
            }
        }

        await uiThreadDispatcher.InvokeAsync(() =>
        {
            try
            {
                if (!disposed && !work.Cancellation.IsCancellationRequested && CurrentInputs is { } inputs && key.Matches(inputs) &&
                    ReferenceEquals(inFlight.GetValueOrDefault(key), work) && change is not null)
                {
                    if (change.Result is { } result) results[key] = result;
                    changes.OnNext(change);
                }
            }
            finally
            {
                if (ReferenceEquals(inFlight.GetValueOrDefault(key), work)) inFlight.Remove(key);
                active.Remove(work);
                work.Cancellation.Dispose();
                work.Completion.TrySetResult();
            }
        });
    }

    private sealed class InFlightAnalysis
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

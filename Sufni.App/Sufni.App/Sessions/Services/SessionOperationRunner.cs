using System;
using System.Collections.Immutable;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Sufni.App.Sessions.Presentation;

namespace Sufni.App.Sessions.Services;

/// <summary>
/// Owns the running operation of each session. A session runs one operation at a
/// time: starting a new one cancels the previous one. Progress is published per
/// session so an open editor can render it.
/// </summary>
public interface ISessionOperationRunner
{
    SessionOperation Start(Guid sessionId, string message);

    void Cancel(Guid sessionId);

    /// <summary>Emits the current state of the session's operation, then every change.</summary>
    IObservable<SessionOperationPresentationState> Watch(Guid sessionId);
}

/// <summary>
/// One running operation. Disposing it completes the operation; reports and
/// completion of an operation that is no longer current are ignored.
/// </summary>
public sealed class SessionOperation : IDisposable
{
    private readonly SessionOperationRunner runner;
    private readonly CancellationTokenSource cancellation = new();

    internal SessionOperation(SessionOperationRunner runner, Guid sessionId)
    {
        this.runner = runner;
        SessionId = sessionId;
    }

    internal Guid SessionId { get; }

    public CancellationToken CancellationToken => cancellation.Token;

    public bool IsCurrent => runner.IsCurrent(this);

    public void Report(string message, double percent) => runner.Report(this, message, percent);

    public void Dispose() => runner.Complete(this);

    internal void Cancel() => cancellation.Cancel();
}

internal sealed class SessionOperationRunner : ISessionOperationRunner
{
    private readonly Lock gate = new();
    private readonly BehaviorSubject<ImmutableDictionary<Guid, SessionOperationPresentationState>> states =
        new(ImmutableDictionary<Guid, SessionOperationPresentationState>.Empty);
    private ImmutableDictionary<Guid, SessionOperation> operations = ImmutableDictionary<Guid, SessionOperation>.Empty;

    public SessionOperation Start(Guid sessionId, string message)
    {
        var operation = new SessionOperation(this, sessionId);
        SessionOperation? previous;
        lock (gate)
        {
            operations.TryGetValue(sessionId, out previous);
            operations = operations.SetItem(sessionId, operation);
            Publish(sessionId, SessionOperationPresentationState.Progress(message, 0));
        }

        previous?.Cancel();
        return operation;
    }

    public void Cancel(Guid sessionId)
    {
        SessionOperation? operation;
        lock (gate)
        {
            if (!operations.TryGetValue(sessionId, out operation))
            {
                return;
            }

            operations = operations.Remove(sessionId);
            Publish(sessionId, null);
        }

        operation.Cancel();
    }

    public IObservable<SessionOperationPresentationState> Watch(Guid sessionId) =>
        states
            .Select(all => all.GetValueOrDefault(sessionId, SessionOperationPresentationState.Hidden))
            .DistinctUntilChanged();

    internal bool IsCurrent(SessionOperation operation)
    {
        lock (gate)
        {
            return IsCurrentLocked(operation);
        }
    }

    internal void Report(SessionOperation operation, string message, double percent)
    {
        lock (gate)
        {
            if (IsCurrentLocked(operation))
            {
                Publish(operation.SessionId, SessionOperationPresentationState.Progress(message, percent));
            }
        }
    }

    internal void Complete(SessionOperation operation)
    {
        lock (gate)
        {
            if (!IsCurrentLocked(operation))
            {
                return;
            }

            operations = operations.Remove(operation.SessionId);
            Publish(operation.SessionId, null);
        }
    }

    private bool IsCurrentLocked(SessionOperation operation) =>
        operations.TryGetValue(operation.SessionId, out var current) && ReferenceEquals(current, operation);

    private void Publish(Guid sessionId, SessionOperationPresentationState? state)
    {
        var current = states.Value;
        states.OnNext(state is null ? current.Remove(sessionId) : current.SetItem(sessionId, state));
    }
}

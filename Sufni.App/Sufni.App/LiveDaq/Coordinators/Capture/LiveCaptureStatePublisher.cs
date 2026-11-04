using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Plots;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

internal sealed class LiveCaptureStatePublisher
{
    private readonly System.Threading.Lock gate = new();
    private readonly IUiThreadDispatcher dispatcher;
    private readonly TimeProvider clock;
    private readonly Func<long> getRevision;
    private readonly Func<LiveCaptureState> build;
    private readonly BehaviorSubject<LiveCaptureState> states = new(LiveCaptureState.Empty);
    private ITimer? timer;
    private int subscribers;
    private bool pending;
    private bool ending;
    private long publishedRevision;
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool completed;

    public LiveCaptureStatePublisher(IUiThreadDispatcher dispatcher, TimeProvider clock,
        Func<long> getRevision, Func<LiveCaptureState> build)
    {
        this.dispatcher = dispatcher;
        this.clock = clock;
        this.getRevision = getRevision;
        this.build = build;
        States = Observable.Create<LiveCaptureState>(observer =>
        {
            var subscription = states.Subscribe(observer);
            lock (gate)
            {
                subscribers++;
                if (!ending && subscribers == 1)
                {
                    var interval = TimeSpan.FromMilliseconds(PlotSettings.LiveUiRefreshIntervalMs);
                    timer = clock.CreateTimer(_ => OnCadence(), null, interval, interval);
                }
            }
            return Disposable.Create(() =>
            {
                subscription.Dispose();
                lock (gate)
                {
                    if (--subscribers == 0)
                    {
                        timer?.Dispose();
                        timer = null;
                    }
                }
            });
        });
    }

    public IObservable<LiveCaptureState> States { get; }
    public LiveCaptureState Current { get; private set; } = LiveCaptureState.Empty;

    public void Request()
    {
        lock (gate)
        {
            if (ending || pending) return;
            pending = true;
        }
        dispatcher.Post(Publish);
    }

    public Task CompleteAsync()
    {
        bool postCompletion;
        lock (gate)
        {
            if (ending) return completion.Task;
            ending = true;
            timer?.Dispose();
            timer = null;
            postCompletion = !pending;
        }
        if (postCompletion) dispatcher.Post(CompletePosted);
        return completion.Task;
    }

    private void CompletePosted()
    {
        lock (gate)
        {
            if (completed) return;
            completed = true;
        }
        states.OnCompleted();
        states.Dispose();
        completion.SetResult();
    }

    private void OnCadence()
    {
        if (getRevision() > Volatile.Read(ref publishedRevision)) Request();
    }

    private void Publish()
    {
        lock (gate) pending = false;
        var state = build();
        if (state.Revision > publishedRevision)
        {
            Current = state;
            Volatile.Write(ref publishedRevision, state.Revision);
            states.OnNext(state);
        }
        bool finish;
        lock (gate) finish = ending && !pending;
        if (finish) CompletePosted();
    }
}

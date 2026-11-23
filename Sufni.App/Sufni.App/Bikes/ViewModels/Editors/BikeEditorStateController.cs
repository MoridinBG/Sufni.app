using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Sufni.App.Bikes.Coordinators;

namespace Sufni.App.Bikes.ViewModels.Editors;

/// <summary>What caused a state change, which decides whether and how the analysis reruns.</summary>
internal enum BikeAnalysisTrigger
{
    /// <summary>A geometry edit. Reruns debounced, with the busy overlay, when the spec changes.</summary>
    Edit,
    /// <summary>A mode or curve edit. Reruns debounced, without the busy overlay, when the spec changes.</summary>
    QuietEdit,
    /// <summary>An opened draft or an attached view. Reruns at once.</summary>
    Load,
    /// <summary>A reset or an accepted conflict reload. Reruns at once, with the busy overlay.</summary>
    Reload,
    /// <summary>An image that replaces an unreadable one. Reruns debounced, with the busy overlay.</summary>
    Recovery
}

internal readonly record struct BikeEditorTransition(BikeEditorState State, BikeAnalysisTrigger? Analysis = null);

public sealed class BikeEditorStateController : IDisposable
{
    private readonly BehaviorSubject<BikeEditorState> states;
    private readonly Queue<(Action Run, Action Abandon)> pending = new();
    private bool publishing;
    private bool disposed;

    public BikeEditorStateController(BikeDraft baseline, bool persisted)
    {
        states = new(new BikeEditorState(baseline, persisted));
        States = states.AsObservable();
        Actions = new(this);
    }

    public BikeEditorActions Actions { get; }
    public BikeEditorState Current => states.Value;
    public IObservable<BikeEditorState> States { get; }

    // Outside Ready, the unchanged state is republished so that two-way bindings return to the state value.
    internal void Edit(Func<BikeEditorState, BikeEditorState> reduce, BikeAnalysisTrigger analysis) => Enqueue(() =>
    {
        var state = Current;
        Publish(state.Phase == BikeEditorPhase.Ready ? Commit(state, new(reduce(state), analysis)) : state);
    }, () => { });

    /// <summary>Applies a controller intent after the current publication reaches all subscribers.</summary>
    /// <returns>Whether the reducer accepted the intent.</returns>
    internal Task<bool> Deliver(Func<BikeEditorState, BikeEditorTransition?> reduce)
    {
        var outcome = new TaskCompletionSource<bool>();
        Enqueue(() =>
        {
            var state = Current;
            var transition = state.Phase == BikeEditorPhase.Closed ? null : reduce(state);
            if (transition is { } accepted) Publish(Commit(state, accepted));
            outcome.SetResult(transition is not null);
        }, () => outcome.SetResult(false));
        return outcome.Task;
    }

    private static BikeEditorState Commit(BikeEditorState before, BikeEditorTransition transition)
    {
        var next = BikeEditorIntentReducers.FollowPhotoHeadAngle(transition.State.Derive());
        if (transition.Analysis is not { } trigger || (!Forces(trigger) && next.Geometry.Spec == before.Geometry.Spec))
            return next;
        var run = !next.AnalysisSuspended;
        return next with
        {
            Request = new(next.Request.Generation + 1, run ? next.Geometry.Spec : null,
                trigger is BikeAnalysisTrigger.Edit or BikeAnalysisTrigger.QuietEdit or BikeAnalysisTrigger.Recovery,
                run && trigger is BikeAnalysisTrigger.Edit or BikeAnalysisTrigger.Reload or BikeAnalysisTrigger.Recovery)
        };
    }

    private static bool Forces(BikeAnalysisTrigger trigger) =>
        trigger is BikeAnalysisTrigger.Load or BikeAnalysisTrigger.Reload or BikeAnalysisTrigger.Recovery;

    private void Enqueue(Action run, Action abandon)
    {
        if (disposed)
        {
            abandon();
            return;
        }
        pending.Enqueue((run, abandon));
        while (!publishing && pending.TryDequeue(out var next)) next.Run();
    }

    private void Publish(BikeEditorState next)
    {
        publishing = true;
        try { states.OnNext(next); }
        finally { publishing = false; }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        while (pending.TryDequeue(out var next)) next.Abandon();
        states.OnCompleted();
    }
}

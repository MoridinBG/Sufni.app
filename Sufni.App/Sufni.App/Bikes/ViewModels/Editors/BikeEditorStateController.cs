using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Reactive.Linq;
using Sufni.App.Bikes.Coordinators;

namespace Sufni.App.Bikes.ViewModels.Editors;

public sealed class BikeEditorStateController : IDisposable
{
    private readonly BehaviorSubject<BikeEditorState> states;
    private readonly Queue<Action> edits = new();
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

    internal void Edit(Func<BikeEditorState, BikeEditorState> reduce, bool busy)
    {
        if (disposed) return;
        edits.Enqueue(() =>
        {
            var state = Current;
            if (state.Phase != BikeEditorPhase.Ready) Publish(state);
            else Publish(Refresh(state, reduce(state), force: false, debounce: true, busy));
        });
        Flush();
    }

    internal bool Deliver(Func<BikeEditorState, BikeEditorState?> reduce)
    {
        if (disposed || publishing || Current.Phase == BikeEditorPhase.Closed || reduce(Current) is not { } next)
            return false;
        Publish(next);
        Flush();
        return true;
    }

    internal static BikeEditorState Refresh(BikeEditorState before, BikeEditorState after, bool force, bool debounce, bool busy)
    {
        var inputs = after.Inputs;
        if (before.Phase == BikeEditorPhase.Ready && before.Geometry.IsHeadAngleDerived &&
            !ReferenceEquals(before.Inputs, inputs))
        {
            inputs = inputs with { ManualHeadAngle = before.Geometry.EffectiveHeadAngle };
        }
        var geometry = BikeEditorGeometryDeriver.Derive(inputs, after.Baseline, after.Presentation);
        if (!geometry.IsHeadAngleDerived) after = after with { Inputs = inputs };
        if (after.Inputs.Mode != before.Inputs.Mode) after = after with { Presentation = null };
        if (force || geometry.Spec != before.Geometry.Spec)
        {
            var run = !after.AnalysisSuspended;
            after = after with { Request = new(before.Request.Generation + 1, run ? geometry.Spec : null, debounce, run && busy) };
        }
        return after with { Geometry = geometry };
    }

    private void Publish(BikeEditorState next)
    {
        publishing = true;
        try { states.OnNext(next); }
        finally { publishing = false; }
    }

    private void Flush()
    {
        while (!publishing && edits.TryDequeue(out var edit)) edit();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        edits.Clear();
        states.OnCompleted();
    }
}

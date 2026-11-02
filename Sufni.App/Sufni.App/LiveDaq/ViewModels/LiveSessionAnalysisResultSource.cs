using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.Sessions.Analysis.Services;

namespace Sufni.App.LiveDaq.ViewModels;

// UI-thread projection of complete, eagerly computed live analysis snapshots.
internal sealed class LiveSessionAnalysisResultSource : ISessionAnalysisResultSource
{
    private readonly Subject<RecordedSessionAnalysisInputs?> inputs = new();
    private LiveSessionAnalysis? current;
    public LiveSessionAnalysis? Current => current;
    public RecordedSessionAnalysisInputs? CurrentInputs => current?.Inputs;
    public IObservable<RecordedSessionAnalysisInputs?> ConnectInputs() => inputs.AsObservable();
    public IObservable<RecordedSessionAnalysisResultChanged> Connect() => Observable.Empty<RecordedSessionAnalysisResultChanged>();
    public RecordedSessionAnalysisResult? Get(RecordedSessionAnalysisKey key) =>
        current is { } snapshot && snapshot.Results.TryGetValue(key, out var result) ? result : null;

    public void Apply(LiveSessionAnalysis? analysis)
    {
        if (ReferenceEquals(current, analysis)) return;
        current = analysis;
        inputs.OnNext(analysis?.Inputs);
    }

    public void Dispose()
    {
        inputs.OnCompleted();
        inputs.Dispose();
    }
}

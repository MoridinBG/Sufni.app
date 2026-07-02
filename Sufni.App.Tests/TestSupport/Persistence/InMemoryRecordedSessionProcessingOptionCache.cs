using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Sufni.Telemetry;

using Sufni.App.Sessions.Processing.RecordedSessionProjection;
namespace Sufni.App.Tests.TestSupport.Persistence;

// Controllable in-memory IRecordedSessionProcessingOptionCache for tests. Get
// mirrors production (a cache miss is the 25 ms default); Set updates the value
// and publishes through OptionChanged so projection/domain reactions to a preference
// change can be exercised deterministically.
internal sealed class InMemoryRecordedSessionProcessingOptionCache : IRecordedSessionProcessingOptionCache
{
    private readonly Dictionary<Guid, TelemetryProcessingOptions> options = [];
    private readonly Subject<Guid> optionChanged = new();

    public IObservable<Guid> OptionChanged => optionChanged.AsObservable();

    public TelemetryProcessingOptions Get(Guid sessionId) =>
        options.TryGetValue(sessionId, out var option) ? option : TelemetryProcessingOptions.Default;

    public Task HydrateAsync() => Task.CompletedTask;

    public void Set(Guid sessionId, TelemetryProcessingOptions option)
    {
        options[sessionId] = option;
        optionChanged.OnNext(sessionId);
    }
}

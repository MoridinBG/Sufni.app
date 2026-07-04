using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Sufni.Telemetry;

using Sufni.App.Sessions.Processing.RecordedSessionProjection;
namespace Sufni.App.Tests.TestSupport.Persistence;

// Lightweight IRecordedSessionProcessingOptionCache for editor tests. Get mirrors
// production's cache-miss behavior; direct cache mutation is covered by
// RecordedSessionProcessingOptionCacheTests.
internal sealed class InMemoryRecordedSessionProcessingOptionCache : IRecordedSessionProcessingOptionCache
{
    public IObservable<Guid> OptionChanged => Observable.Empty<Guid>();

    public TelemetryProcessingOptions Get(Guid sessionId) => TelemetryProcessingOptions.Default;

    public Task HydrateAsync() => Task.CompletedTask;
}

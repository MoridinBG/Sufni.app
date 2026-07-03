using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Sessions.Processing.RecordedSessionProjection;
namespace Sufni.App.Tests.TestSupport.Persistence;

// Controllable in-memory IRecordedSessionDerivationWindowCache for projection tests.
// Set publishes through WindowChanged so window->projection reactions can be exercised
// deterministically without extension services.
internal sealed class InMemoryRecordedSessionDerivationWindowCache : IRecordedSessionDerivationWindowCache
{
    private readonly Dictionary<Guid, RecordedSessionDerivationWindow> windows = [];
    private readonly Subject<Guid> windowChanged = new();

    public IObservable<Guid> WindowChanged => windowChanged.AsObservable();

    public RecordedSessionDerivationWindow? Get(Guid sessionId) =>
        windows.GetValueOrDefault(sessionId);

    public IReadOnlyCollection<Guid> GetSessionIdsReferencingSource(Guid sourceSessionId) =>
        windows
            .Where(pair => pair.Value.SourceSessionId == sourceSessionId)
            .Select(pair => pair.Key)
            .ToArray();

    public Task HydrateAsync() => Task.CompletedTask;

    public Task RefreshSessionAsync(Guid sessionId) => Task.CompletedTask;

    public void Set(Guid sessionId, RecordedSessionDerivationWindow? window)
    {
        if (window is null)
        {
            windows.Remove(sessionId);
        }
        else
        {
            windows[sessionId] = window;
        }

        windowChanged.OnNext(sessionId);
    }
}

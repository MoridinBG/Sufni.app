using System.Reactive.Linq;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;

namespace Sufni.App.Tests.Sessions.Processing.RecordedSessionProjection;

public class RecordedSessionDerivationWindowCacheTests
{
    [Fact]
    public async Task HydrateAsync_LoadsWindowsAndReverseMapWithoutFirstEmission()
    {
        var sessionId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var provider = new TestWindowProvider();
        provider.Windows[sessionId] = new RecordedSessionDerivationWindow(sourceId, 1, 2);
        var cache = new RecordedSessionDerivationWindowCache(provider);
        var changed = new List<Guid>();
        using var subscription = cache.WindowChanged.Subscribe(changed.Add);

        await cache.HydrateAsync();

        Assert.Empty(changed);
        Assert.Equal(provider.Windows[sessionId], cache.Get(sessionId));
        Assert.Equal([sessionId], cache.GetSessionIdsReferencingSource(sourceId));
    }

    [Fact]
    public async Task HydrateAsync_EmitsChangedAndRemovedSessions_AfterFirstHydration()
    {
        var changedSessionId = Guid.NewGuid();
        var removedSessionId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var provider = new TestWindowProvider();
        provider.Windows[changedSessionId] = new RecordedSessionDerivationWindow(sourceId, 1, 2);
        provider.Windows[removedSessionId] = new RecordedSessionDerivationWindow(sourceId, 3, 4);
        var cache = new RecordedSessionDerivationWindowCache(provider);
        await cache.HydrateAsync();
        var changed = new List<Guid>();
        using var subscription = cache.WindowChanged.Subscribe(changed.Add);

        provider.Windows[changedSessionId] = new RecordedSessionDerivationWindow(sourceId, 1, 5);
        provider.Windows.Remove(removedSessionId);
        await cache.HydrateAsync();

        Assert.Equal(2, changed.Count);
        Assert.Contains(changedSessionId, changed);
        Assert.Contains(removedSessionId, changed);
        Assert.Equal(provider.Windows[changedSessionId], cache.Get(changedSessionId));
        Assert.Null(cache.Get(removedSessionId));
        Assert.Equal([changedSessionId], cache.GetSessionIdsReferencingSource(sourceId));
    }

    [Fact]
    public async Task RefreshSessionAsync_UpdatesOneSessionAndEmitsWhenChanged()
    {
        var sessionId = Guid.NewGuid();
        var oldSourceId = Guid.NewGuid();
        var newSourceId = Guid.NewGuid();
        var provider = new TestWindowProvider();
        provider.Windows[sessionId] = new RecordedSessionDerivationWindow(oldSourceId, 1, 2);
        var cache = new RecordedSessionDerivationWindowCache(provider);
        await cache.HydrateAsync();
        var changed = new List<Guid>();
        using var subscription = cache.WindowChanged.Subscribe(changed.Add);

        provider.Windows[sessionId] = new RecordedSessionDerivationWindow(newSourceId, 3, null);
        await cache.RefreshSessionAsync(sessionId);

        Assert.Equal([sessionId], changed);
        Assert.Equal(provider.Windows[sessionId], cache.Get(sessionId));
        Assert.Empty(cache.GetSessionIdsReferencingSource(oldSourceId));
        Assert.Equal([sessionId], cache.GetSessionIdsReferencingSource(newSourceId));
    }

    [Fact]
    public async Task ProviderWindowsChanged_RehydratesAndPublishesDiff()
    {
        var sessionId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var provider = new TestWindowProvider();
        var cache = new RecordedSessionDerivationWindowCache(provider);
        await cache.HydrateAsync();
        var changed = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = cache.WindowChanged.Subscribe(sessionId => changed.TrySetResult(sessionId));

        provider.Windows[sessionId] = new RecordedSessionDerivationWindow(sourceId, 1, 2);
        provider.RaiseWindowsChanged();

        Assert.Equal(sessionId, await changed.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Equal(provider.Windows[sessionId], cache.Get(sessionId));
    }

    private sealed class TestWindowProvider : IRecordedSessionDerivationWindowProvider
    {
        public Dictionary<Guid, RecordedSessionDerivationWindow> Windows { get; } = [];
        public event EventHandler? WindowsChanged;

        public Task<RecordedSessionDerivationWindow?> GetWindowAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Windows.GetValueOrDefault(sessionId));
        }

        public Task<IReadOnlyDictionary<Guid, RecordedSessionDerivationWindow>> GetWindowsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<Guid, RecordedSessionDerivationWindow>>(new Dictionary<Guid, RecordedSessionDerivationWindow>(Windows));
        }

        public Task<bool> IsRecordingSourceReferencedAsync(
            Guid sourceSessionId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Windows.Values.Any(window => window.SourceSessionId == sourceSessionId));
        }

        public Task<IReadOnlyCollection<Guid>> GetReferencedSourceSessionIdsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<Guid>>(
                Windows.Values.Select(window => window.SourceSessionId).Distinct().ToArray());
        }

        public void RaiseWindowsChanged()
        {
            WindowsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

public interface IRecordedSessionDerivationWindowCache
{
    RecordedSessionDerivationWindow? Get(Guid sessionId);
    IReadOnlyCollection<Guid> GetSessionIdsReferencingSource(Guid sourceSessionId);
    IObservable<Guid> WindowChanged { get; }
    Task HydrateAsync();
    Task RefreshSessionAsync(Guid sessionId);
}

internal sealed class RecordedSessionDerivationWindowCache : IRecordedSessionDerivationWindowCache, IDisposable
{
    private readonly IRecordedSessionDerivationWindowProvider windowProvider;
    private readonly Dictionary<Guid, RecordedSessionDerivationWindow> windowsBySession = [];
    private readonly Dictionary<Guid, HashSet<Guid>> sessionIdsBySource = [];
    private readonly System.Threading.Lock gate = new();
    private readonly Subject<Guid> windowChanged = new();
    private bool hydrated;

    public RecordedSessionDerivationWindowCache(IRecordedSessionDerivationWindowProvider windowProvider)
    {
        this.windowProvider = windowProvider;
        this.windowProvider.WindowsChanged += OnProviderWindowsChanged;
    }

    public IObservable<Guid> WindowChanged => windowChanged.AsObservable();

    public RecordedSessionDerivationWindow? Get(Guid sessionId)
    {
        lock (gate)
        {
            return windowsBySession.GetValueOrDefault(sessionId);
        }
    }

    public IReadOnlyCollection<Guid> GetSessionIdsReferencingSource(Guid sourceSessionId)
    {
        lock (gate)
        {
            return sessionIdsBySource.TryGetValue(sourceSessionId, out var sessionIds)
                ? sessionIds.ToArray()
                : [];
        }
    }

    public async Task HydrateAsync()
    {
        var fresh = await windowProvider.GetWindowsAsync();
        var changed = new List<Guid>();

        lock (gate)
        {
            if (hydrated)
            {
                foreach (var (sessionId, window) in fresh)
                {
                    if (!windowsBySession.TryGetValue(sessionId, out var previous) || previous != window)
                    {
                        changed.Add(sessionId);
                    }
                }

                foreach (var sessionId in windowsBySession.Keys)
                {
                    if (!fresh.ContainsKey(sessionId))
                    {
                        changed.Add(sessionId);
                    }
                }
            }

            windowsBySession.Clear();
            foreach (var (sessionId, window) in fresh)
            {
                windowsBySession[sessionId] = window;
            }

            RebuildReverseMapLocked();
            hydrated = true;
        }

        PublishChanged(changed);
    }

    public async Task RefreshSessionAsync(Guid sessionId)
    {
        var fresh = await windowProvider.GetWindowAsync(sessionId);
        var changed = false;

        lock (gate)
        {
            windowsBySession.TryGetValue(sessionId, out var previous);
            changed = previous != fresh;

            RemoveReverseMapEntryLocked(sessionId, previous);
            if (fresh is null)
            {
                windowsBySession.Remove(sessionId);
            }
            else
            {
                windowsBySession[sessionId] = fresh;
                AddReverseMapEntryLocked(sessionId, fresh);
            }
        }

        if (changed)
        {
            windowChanged.OnNext(sessionId);
        }
    }

    public void Dispose()
    {
        windowProvider.WindowsChanged -= OnProviderWindowsChanged;
        windowChanged.Dispose();
    }

    private void OnProviderWindowsChanged(object? sender, EventArgs args)
    {
        _ = HydrateAsync();
    }

    private void RebuildReverseMapLocked()
    {
        sessionIdsBySource.Clear();
        foreach (var (sessionId, window) in windowsBySession)
        {
            AddReverseMapEntryLocked(sessionId, window);
        }
    }

    private void AddReverseMapEntryLocked(Guid sessionId, RecordedSessionDerivationWindow window)
    {
        if (!sessionIdsBySource.TryGetValue(window.SourceSessionId, out var sessionIds))
        {
            sessionIds = [];
            sessionIdsBySource[window.SourceSessionId] = sessionIds;
        }

        sessionIds.Add(sessionId);
    }

    private void RemoveReverseMapEntryLocked(Guid sessionId, RecordedSessionDerivationWindow? window)
    {
        if (window is null ||
            !sessionIdsBySource.TryGetValue(window.SourceSessionId, out var sessionIds))
        {
            return;
        }

        sessionIds.Remove(sessionId);
        if (sessionIds.Count == 0)
        {
            sessionIdsBySource.Remove(window.SourceSessionId);
        }
    }

    private void PublishChanged(IEnumerable<Guid> sessionIds)
    {
        foreach (var sessionId in sessionIds.Distinct())
        {
            windowChanged.OnNext(sessionId);
        }
    }
}

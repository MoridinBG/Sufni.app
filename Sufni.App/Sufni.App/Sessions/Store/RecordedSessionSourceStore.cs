using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Services;
using Sufni.App.Shared.Base;
namespace Sufni.App.Sessions.Store;

/// <summary>
/// Reactive metadata cache for recorded-session raw sources.
/// It keeps source identity and hash information in a DynamicData cache and
/// leaves payload retrieval to explicit load calls.
/// </summary>
internal sealed class RecordedSessionSourceStore(
    IRecordedSessionSourceRepository sourceRepository,
    IUiThreadDispatcher uiThreadDispatcher)
    : SourceCacheStoreBase<RecordedSessionSourceSnapshot, Guid>(s => s.SessionId, uiThreadDispatcher), IRecordedSessionSourceStoreWriter
{
    public Task<RecordedSessionSource?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return sourceRepository.GetRecordedSessionSourceAsync(sessionId);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sources = await sourceRepository.GetRecordedSessionSourceSnapshotsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        await ReplaceWithAsync(sources);
    }

    public async Task PublishSourcesChangedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default)
    {
        var snapshots = new List<RecordedSessionSourceSnapshot>();
        var removedIds = new List<Guid>();

        foreach (var sessionId in sessionIds.Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await sourceRepository.GetRecordedSessionSourceSnapshotAsync(sessionId);
            if (snapshot is null)
            {
                removedIds.Add(sessionId);
            }
            else
            {
                snapshots.Add(snapshot);
            }
        }

        if (snapshots.Count > 0)
        {
            await PublishSnapshotsAsync(snapshots);
        }

        if (removedIds.Count > 0)
        {
            await PublishRemovalsAsync(removedIds);
        }
    }

    public Task PublishSourcesRemovedAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return PublishRemovalsAsync(sessionIds.Distinct());
    }

}

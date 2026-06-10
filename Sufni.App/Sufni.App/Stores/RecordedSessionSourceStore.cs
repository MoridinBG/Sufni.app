using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Stores;

/// <summary>
/// Reactive metadata cache for recorded-session raw sources.
/// It keeps source identity and hash information in a DynamicData cache and
/// leaves payload retrieval to explicit load calls.
/// </summary>
internal sealed class RecordedSessionSourceStore(IRecordedSessionSourceRepository sourceRepository)
    : SourceCacheStoreBase<RecordedSessionSourceSnapshot, Guid>(s => s.SessionId), IRecordedSessionSourceStoreWriter
{
    public Task<RecordedSessionSource?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return sourceRepository.GetRecordedSessionSourceAsync(sessionId);
    }

    public async Task RefreshAsync()
    {
        var sources = await sourceRepository.GetRecordedSessionSourcesAsync();
        ReplaceWith(sources.Select(RecordedSessionSourceSnapshot.From));
    }

}

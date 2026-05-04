using System;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Stores;

internal sealed class RecordedSessionSourceStore(IDatabaseService databaseService) : IRecordedSessionSourceStoreWriter
{
    private readonly SourceCache<RecordedSessionSourceSnapshot, Guid> source = new(s => s.SessionId);

    public IObservable<IChangeSet<RecordedSessionSourceSnapshot, Guid>> Connect() => source.Connect();

    public RecordedSessionSourceSnapshot? Get(Guid sessionId)
    {
        var lookup = source.Lookup(sessionId);
        return lookup.HasValue ? lookup.Value : null;
    }

    public Task<RecordedSessionSource?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return databaseService.GetRecordedSessionSourceAsync(sessionId);
    }

    public async Task RefreshAsync()
    {
        var sources = await databaseService.GetRecordedSessionSourcesAsync();
        source.Edit(cache =>
        {
            cache.Clear();
            foreach (var recordedSource in sources)
            {
                cache.AddOrUpdate(RecordedSessionSourceSnapshot.From(recordedSource));
            }
        });
    }

    public async Task SaveAsync(RecordedSessionSource recordedSource, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await databaseService.PutRecordedSessionSourceAsync(recordedSource);
        source.AddOrUpdate(RecordedSessionSourceSnapshot.From(recordedSource));
    }

    public async Task RemoveAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await databaseService.DeleteRecordedSessionSourceAsync(sessionId);
        source.RemoveKey(sessionId);
    }

    public void Upsert(RecordedSessionSourceSnapshot snapshot) => source.AddOrUpdate(snapshot);

    public void Remove(Guid sessionId) => source.RemoveKey(sessionId);
}

using System;
using System.Threading.Tasks;
using DynamicData;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Stores;

/// <summary>
/// Single source of truth for "what bikes exist". Loaded once at startup
/// and updated by coordinators via <see cref="IBikeStoreWriter"/>.
/// Registered as a singleton behind both <see cref="IBikeStore"/> and
/// <see cref="IBikeStoreWriter"/>.
/// </summary>
internal sealed class BikeStore(IDatabaseService databaseService) : IBikeStoreWriter
{
    private readonly SourceCache<BikeSnapshot, Guid> source = new(b => b.Id);

    public IObservable<IChangeSet<BikeSnapshot, Guid>> Connect() => source.Connect();

    public BikeSnapshot? Get(Guid id)
    {
        var lookup = source.Lookup(id);
        return lookup.HasValue ? lookup.Value : null;
    }

    public async Task RefreshAsync()
    {
        var bikes = await databaseService.GetAllAsync<Bike>();
        source.Edit(cache =>
        {
            cache.Clear();
            foreach (var bike in bikes)
            {
                cache.AddOrUpdate(BikeSnapshot.From(bike));
            }
        });
    }

    public void Upsert(BikeSnapshot snapshot) => source.AddOrUpdate(snapshot);

    public void Remove(Guid id) => source.RemoveKey(id);
}

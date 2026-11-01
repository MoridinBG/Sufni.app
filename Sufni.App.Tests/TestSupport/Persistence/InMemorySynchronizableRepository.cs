using System.Diagnostics.CodeAnalysis;
using Sufni.App.SyncAndPairing.Models;
using Sufni.App.SyncAndPairing.Services;

namespace Sufni.App.Tests.TestSupport.Persistence;

internal sealed class InMemorySynchronizableRepository<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] T>(TimeProvider clock)
    : ISynchronizableRepository<T> where T : Synchronizable, new()
{
    private readonly Dictionary<Guid, T> items = [];
    public Exception? WriteFailure { get; set; }
    public Func<Task>? BeforeWrite { get; set; }

    public void Seed(T item) => items[item.Id] = item;
    public Task<List<T>> GetAllAsync() => Task.FromResult(items.Values.Where(item => item.Deleted is null).ToList());
    public Task<List<T>> GetChangedAsync(long sinceExclusive, long upperInclusive) => Task.FromResult(items.Values
        .Where(item => item.Updated > sinceExclusive && item.Updated <= upperInclusive ||
                       item.Deleted > sinceExclusive && item.Deleted <= upperInclusive).ToList());
    public Task<T?> GetAsync(Guid id) => Task.FromResult(items.GetValueOrDefault(id) is { Deleted: null } item ? item : null);

    public async Task<Guid> PutAsync(T item)
    {
        if (BeforeWrite is { } beforeWrite) await beforeWrite();
        if (WriteFailure is { } failure) throw failure;
        item.Updated = clock.GetUtcNow().ToUnixTimeSeconds();
        item.Deleted = null;
        items[item.Id] = item;
        return item.Id;
    }

    public Task DeleteAsync(T item) => DeleteAsync(item.Id);
    public Task DeleteAsync(Guid id)
    {
        if (WriteFailure is { } failure) return Task.FromException(failure);
        if (items.GetValueOrDefault(id) is { Deleted: null } item)
            item.Deleted = clock.GetUtcNow().ToUnixTimeSeconds();
        return Task.CompletedTask;
    }
}

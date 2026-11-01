using System.Reactive.Linq;
using System.Reactive.Subjects;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.TestSupport.Doubles;

internal sealed class InMemoryMapPreferences : IMapPreferences, IDisposable
{
    private readonly BehaviorSubject<Guid?> changes = new(null);
    public Guid? SelectedLayerId => changes.Value;
    public Exception? WriteFailure { get; set; }
    public Func<Task>? BeforeWrite { get; set; }
    public Task<Guid?> GetSelectedLayerIdAsync() => Task.FromResult(SelectedLayerId);
    public IObservable<Guid?> ObserveChanges() => changes.AsObservable();
    public void Seed(Guid? id) => changes.OnNext(id);

    public async Task SetSelectedLayerIdAsync(Guid id)
    {
        if (BeforeWrite is { } beforeWrite) await beforeWrite();
        if (WriteFailure is { } failure) throw failure;
        changes.OnNext(id);
    }

    public void Dispose() => changes.Dispose();
}

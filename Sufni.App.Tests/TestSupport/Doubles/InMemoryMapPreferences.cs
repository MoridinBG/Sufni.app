using System.Reactive.Linq;
using System.Reactive.Subjects;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;

namespace Sufni.App.Tests.TestSupport.Doubles;

internal sealed class InMemoryMapPreferences : IMapPreferences, IDisposable
{
    private readonly BehaviorSubject<PreferenceValueChange<MapPreferencesValue>> changes = new(
        new(new(null, []), PreferenceChangeOrigin.LocalWrite, false));

    public MapPreferencesValue Value => changes.Value.Value;
    public Exception? WriteFailure { get; set; }
    public Func<Task>? BeforeWrite { get; set; }

    public Task<Guid?> GetSelectedLayerIdAsync() => Task.FromResult(Value.SelectedLayerId);
    public Task<IReadOnlyList<TileLayerConfig>> GetCustomLayersAsync() => Task.FromResult(Value.CustomLayers);
    public IObservable<PreferenceValueChange<MapPreferencesValue>> ObserveChanges() => changes.AsObservable();

    public async Task SetSelectedLayerIdAsync(Guid selectedLayerId)
    {
        await PrepareWriteAsync();
        changes.OnNext(new(Value with { SelectedLayerId = selectedLayerId }, PreferenceChangeOrigin.LocalWrite, true));
    }

    public async Task SetCustomLayersAsync(IReadOnlyList<TileLayerConfig> customLayers)
    {
        await PrepareWriteAsync();
        changes.OnNext(new(Value with { CustomLayers = customLayers }, PreferenceChangeOrigin.LocalWrite, true));
    }

    public void ApplySynced(MapPreferencesValue value) =>
        changes.OnNext(new(value, PreferenceChangeOrigin.SyncApply, true));

    private async Task PrepareWriteAsync()
    {
        if (BeforeWrite is { } beforeWrite) await beforeWrite();
        if (WriteFailure is { } failure) throw failure;
    }

    public void Dispose() => changes.Dispose();
}

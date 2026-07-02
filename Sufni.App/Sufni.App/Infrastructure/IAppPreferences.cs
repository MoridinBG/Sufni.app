using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Theming;
namespace Sufni.App.Infrastructure;

public interface IAppPreferences
{
    IMapPreferences Map { get; }
    ISessionPreferences Session { get; }
    IThemePreferences Theme { get; }
    Task<AppPreferencesSyncData?> GetSyncDataAsync(long since);
    Task ApplySyncDataAsync(AppPreferencesSyncData? preferences);

    // Fires once per successful remote sync apply. Hot observable: subscribers
    // only see emissions that happen after they subscribe. Local writes do not
    // emit through this — view models drive those directly.
    IObservable<Unit> SyncDataApplied { get; }
}

public interface IMapPreferences
{
    Task<Guid?> GetSelectedLayerIdAsync();
    Task SetSelectedLayerIdAsync(Guid selectedLayerId);
    Task<IReadOnlyList<TileLayerConfig>> GetCustomLayersAsync();
    Task SetCustomLayersAsync(IReadOnlyList<TileLayerConfig> customLayers);
}

public interface IThemePreferences
{
    Task<SufniThemeMode> GetModeAsync();
    Task SetModeAsync(SufniThemeMode mode);
}

public interface ISessionPreferences
{
    Task<SessionPreferences> GetRecordedAsync(Guid sessionId);

    // Bulk read of every recorded session's preferences from the single in-memory
    // preferences document. Used to hydrate the processing-option cache for all
    // sessions (list rows, the startup projection sweep, the migration), not just open
    // ones. Sessions absent from the document fall back to defaults at read time.
    Task<IReadOnlyDictionary<Guid, SessionPreferences>> GetAllRecordedAsync();

    Task UpdateRecordedAsync(Guid sessionId, Func<SessionPreferences, SessionPreferences> update);
    Task RemoveRecordedAsync(Guid sessionId);

    // Resets only the processing option for a recorded session to the default,
    // writing WITHOUT advancing the synced preferences document's clock. Used by
    // the one-time startup normalization so a local reset does not win
    // whole-document last-writer-wins sync and overwrite peers' unrelated map,
    // theme, layout, or plot preferences.
    Task ResetRecordedProcessingToDefaultLocallyAsync(Guid sessionId);

    // Emits whenever remote sync writes a (potentially) new value for this
    // session. Cold: no replay of the current value on subscribe — pair with
    // GetRecordedAsync if you need the initial read.
    IObservable<SessionPreferences> ObserveRecorded(Guid sessionId);
}

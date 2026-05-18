using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Theming;

namespace Sufni.App.Services;

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
    Task UpdateRecordedAsync(Guid sessionId, Func<SessionPreferences, SessionPreferences> update);
    Task RemoveRecordedAsync(Guid sessionId);

    // Emits whenever remote sync writes a (potentially) new value for this
    // session. Cold: no replay of the current value on subscribe — pair with
    // GetRecordedAsync if you need the initial read.
    IObservable<SessionPreferences> ObserveRecorded(Guid sessionId);
}

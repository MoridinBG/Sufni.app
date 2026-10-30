using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Infrastructure;

public enum PreferenceChangeOrigin
{
    LocalWrite,
    SyncApply
}

public sealed record PreferenceValueChange<T>(
    T Value,
    PreferenceChangeOrigin Origin,
    bool AdvancesSyncClock);

public sealed record MapPreferencesValue(
    Guid? SelectedLayerId,
    IReadOnlyList<TileLayerConfig> CustomLayers);

public interface IAppPreferences
{
    IMapPreferences Map { get; }
    ISessionPreferences Session { get; }
    IThemePreferences Theme { get; }
    IUiPreferences Ui { get; }
    Task<AppPreferencesSyncData?> GetSyncDataAsync(long sinceExclusive, long upperInclusive);
    Task ApplySyncDataAsync(AppPreferencesSyncData? preferences);
}

public interface IUiPreferences
{
    Task SetLayoutProfileAsync(UiLayoutProfile? layoutProfile);
}

public interface IMapPreferences
{
    Task<Guid?> GetSelectedLayerIdAsync();
    Task SetSelectedLayerIdAsync(Guid selectedLayerId);
    Task<IReadOnlyList<TileLayerConfig>> GetCustomLayersAsync();
    Task SetCustomLayersAsync(IReadOnlyList<TileLayerConfig> customLayers);
    IObservable<PreferenceValueChange<MapPreferencesValue>> ObserveChanges();
}

public interface IThemePreferences
{
    Task<SufniThemeMode> GetModeAsync();
    Task SetModeAsync(SufniThemeMode mode);
    IObservable<PreferenceValueChange<SufniThemeMode>> ObserveModeChanges();
}

public interface ISessionPreferences
{
    Task<SessionPreferences> GetRecordedAsync(Guid sessionId);

    Task UpdateRecordedAsync(Guid sessionId, Func<SessionPreferences, SessionPreferences> update);
    Task RemoveRecordedAsync(Guid sessionId);

    IObservable<SessionPreferences> ObserveRecorded(Guid sessionId);
}

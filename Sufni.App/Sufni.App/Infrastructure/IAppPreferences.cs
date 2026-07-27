using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Theming;
namespace Sufni.App.Infrastructure;

public enum PreferenceChangeOrigin
{
    LocalWrite,
    LocalNoSyncClockWrite,
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
    Task<UiPreferences> GetAsync();
    Task SetLayoutProfileAsync(UiLayoutProfile? layoutProfile);
    IObservable<PreferenceValueChange<UiPreferences>> ObserveChanges();
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

    // Replays the current value and emits local writes, local no-sync-clock
    // writes, and remote sync applies.
    IObservable<SessionPreferences> ObserveRecorded(Guid sessionId);
    IObservable<PreferenceValueChange<SessionPreferences>> ObserveRecordedChanges(Guid sessionId);
    IObservable<PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>> ObserveAllRecordedChanges();
}

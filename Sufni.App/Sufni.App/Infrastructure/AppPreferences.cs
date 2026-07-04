using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Theming;
namespace Sufni.App.Infrastructure;

public sealed class AppPreferences : IAppPreferences
{
    private const int CurrentVersion = AppPreferenceSerialization.CurrentVersion;
    private static readonly ILogger logger = Log.ForContext<AppPreferences>();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly string filePath;
    private readonly int documentVersion;
    private readonly SemaphoreSlim gate = new(1, 1);
    private AppPreferencesDocument document;

    // Hot stream of "remote sync just landed in the on-disk document". Fired
    // after the gate is released so subscribers can re-read without contending
    // for it. Initial-value-less: subscribers care about future emissions, not
    // a snapshot of "has sync ever happened".
    private readonly Subject<Unit> syncDataAppliedSubject = new();
    private readonly Subject<PreferenceValueChange<MapPreferencesValue>> mapPreferencesSubject = new();
    private readonly Subject<PreferenceValueChange<SufniThemeMode>> themePreferencesSubject = new();
    private readonly Subject<PreferenceValueChange<UiPreferences>> uiPreferencesSubject = new();
    private readonly object recordedPreferenceSubjectsGate = new();
    private readonly Dictionary<Guid, Subject<PreferenceValueChange<SessionPreferences>>> recordedPreferenceSubjects = [];
    private readonly Subject<PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>> allRecordedPreferencesSubject = new();

    public IMapPreferences Map { get; }
    public ISessionPreferences Session { get; }
    public IThemePreferences Theme { get; }
    public IUiPreferences Ui { get; }
    public IObservable<Unit> SyncDataApplied => syncDataAppliedSubject.AsObservable();

    public AppPreferences()
        : this(Path.Combine(Path.GetDirectoryName(AppPaths.DatabasePath)!, "app-preferences.json"))
    {
    }

    internal AppPreferences(string filePath)
        : this(filePath, CurrentVersion)
    {
    }

    internal AppPreferences(string filePath, int documentVersion)
    {
        this.filePath = filePath;
        this.documentVersion = documentVersion;
        document = ReadDocumentCore();
        Map = new MapPreferences(this);
        Session = new RecordedSessionPreferences(this);
        Theme = new ThemePreferences(this);
        Ui = new UiPreferenceStore(this);
    }

    private async Task<TResult> ReadAsync<TResult>(Func<AppPreferencesDocument, TResult> read)
    {
        await gate.WaitAsync();
        try
        {
            return read(document);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<AppPreferencesSyncData?> GetSyncDataAsync(long since)
    {
        await gate.WaitAsync();
        try
        {
            if (document.Updated <= 0 && document.HasUserPreferences())
            {
                var next = CloneDocument();
                next.Updated = GetCurrentTimestamp();
                await WriteDocumentCoreAsync(next);
                document = next;
            }

            return document.Updated > since
                ? document.ToSyncData()
                : null;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task ApplySyncDataAsync(AppPreferencesSyncData? preferences)
    {
        if (preferences is null)
        {
            return;
        }

        // Apply under the gate, signal outside it. The signal is what
        // downstream services (TileLayerService, SessionDetailViewModel) use
        // to know the JSON file has new content and they should re-read.
        var applied = false;
        await gate.WaitAsync();
        try
        {
            if (preferences.Updated < document.Updated)
            {
                return;
            }

            var next = CloneDocument();
            next.ApplySyncData(preferences, documentVersion);
            await WriteDocumentCoreAsync(next);
            document = next;
            PublishMapPreferenceChange(
                next.Maps.ToModel(),
                PreferenceChangeOrigin.SyncApply,
                advancesSyncClock: false);
            PublishThemePreferenceChange(
                next.Theme.GetMode(),
                PreferenceChangeOrigin.SyncApply,
                advancesSyncClock: false);
            var recordedPreferences = next.Session.ToModelDictionary();
            var recordedChanges = GetObservedRecordedSessionIds()
                .Select(sessionId => (
                    sessionId,
                    recordedPreferences.TryGetValue(sessionId, out var value)
                        ? value
                        : SessionPreferences.Default))
                .ToArray();
            PublishRecordedPreferenceChanges(
                recordedPreferences,
                PreferenceChangeOrigin.SyncApply,
                advancesSyncClock: false,
                recordedChanges);
            applied = true;
        }
        finally
        {
            gate.Release();
        }

        if (applied)
        {
            syncDataAppliedSubject.OnNext(Unit.Default);
        }
    }

    private async Task UpdateAsync(Action<AppPreferencesDocument> update)
    {
        await gate.WaitAsync();
        try
        {
            var next = CloneDocument();
            update(next);
            next.Updated = GetCurrentTimestamp();
            await WriteDocumentCoreAsync(next);
            document = next;
        }
        finally
        {
            gate.Release();
        }
    }

    // Persists a local/no-bump change WITHOUT advancing the document's sync clock.
    // This keeps preferences that are intentionally excluded from sync, or
    // per-device normalizations, from overwriting peers' synced preferences.
    private async Task UpdateLocalAsync(Action<AppPreferencesDocument> update)
    {
        await gate.WaitAsync();
        try
        {
            var next = CloneDocument();
            update(next);
            await WriteDocumentCoreAsync(next);
            document = next;
        }
        finally
        {
            gate.Release();
        }
    }

    private Subject<PreferenceValueChange<SessionPreferences>> GetRecordedPreferenceSubject(Guid sessionId)
    {
        lock (recordedPreferenceSubjectsGate)
        {
            if (!recordedPreferenceSubjects.TryGetValue(sessionId, out var subject))
            {
                subject = new Subject<PreferenceValueChange<SessionPreferences>>();
                recordedPreferenceSubjects[sessionId] = subject;
            }

            return subject;
        }
    }

    private Guid[] GetObservedRecordedSessionIds()
    {
        lock (recordedPreferenceSubjectsGate)
        {
            return recordedPreferenceSubjects.Keys.ToArray();
        }
    }

    private void PublishRecordedPreferenceChanges(
        IReadOnlyDictionary<Guid, SessionPreferences> allRecorded,
        PreferenceChangeOrigin origin,
        bool advancesSyncClock,
        params (Guid SessionId, SessionPreferences Value)[] recordedChanges)
    {
        foreach (var (sessionId, value) in recordedChanges)
        {
            Subject<PreferenceValueChange<SessionPreferences>>? subject;
            lock (recordedPreferenceSubjectsGate)
            {
                recordedPreferenceSubjects.TryGetValue(sessionId, out subject);
            }

            subject?.OnNext(new PreferenceValueChange<SessionPreferences>(
                value,
                origin,
                advancesSyncClock));
        }

        allRecordedPreferencesSubject.OnNext(
            new PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>(
                allRecorded,
                origin,
                advancesSyncClock));
    }

    private void PublishMapPreferenceChange(
        MapPreferencesValue value,
        PreferenceChangeOrigin origin,
        bool advancesSyncClock)
    {
        mapPreferencesSubject.OnNext(new PreferenceValueChange<MapPreferencesValue>(
            value,
            origin,
            advancesSyncClock));
    }

    private void PublishThemePreferenceChange(
        SufniThemeMode value,
        PreferenceChangeOrigin origin,
        bool advancesSyncClock)
    {
        themePreferencesSubject.OnNext(new PreferenceValueChange<SufniThemeMode>(
            value,
            origin,
            advancesSyncClock));
    }

    private void PublishUiPreferenceChange(
        UiPreferences value,
        PreferenceChangeOrigin origin,
        bool advancesSyncClock)
    {
        uiPreferencesSubject.OnNext(new PreferenceValueChange<UiPreferences>(
            value,
            origin,
            advancesSyncClock));
    }

    private static TileLayerConfig CloneTileLayerConfig(TileLayerConfig layer)
    {
        return new TileLayerConfig
        {
            Id = layer.Id,
            Name = layer.Name,
            UrlTemplate = layer.UrlTemplate,
            AttributionText = layer.AttributionText,
            AttributionUrl = layer.AttributionUrl,
            MaxZoom = layer.MaxZoom,
            IsCustom = layer.IsCustom,
        };
    }

    private static long GetCurrentTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private AppPreferencesDocument CloneDocument()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        return JsonSerializer.Deserialize<AppPreferencesDocument>(bytes, JsonOptions)?.Normalize(documentVersion)
            ?? new AppPreferencesDocument().Normalize(documentVersion);
    }

    private AppPreferencesDocument ReadDocumentCore()
    {
        if (!File.Exists(filePath))
        {
            return new AppPreferencesDocument().Normalize(documentVersion);
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var document = JsonSerializer.Deserialize<AppPreferencesDocument>(stream, JsonOptions);
            return document?.Normalize(documentVersion) ?? new AppPreferencesDocument().Normalize(documentVersion);
        }
        catch (JsonException ex)
        {
            logger.Warning(ex, "Failed to read app preferences JSON at {PreferencesPath}; using defaults", filePath);
            return new AppPreferencesDocument().Normalize(documentVersion);
        }
    }

    private async Task WriteDocumentCoreAsync(AppPreferencesDocument document)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(directory ?? string.Empty, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, document.Normalize(documentVersion), JsonOptions);
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private sealed class MapPreferences(AppPreferences owner) : IMapPreferences
    {
        public Task<Guid?> GetSelectedLayerIdAsync()
        {
            return owner.ReadAsync(document =>
                Guid.TryParse(document.Maps.SelectedLayerId, out var selectedLayerId)
                    ? selectedLayerId
                    : (Guid?)null);
        }

        public Task SetSelectedLayerIdAsync(Guid selectedLayerId)
        {
            return SetAsync(document =>
                document.Maps.SelectedLayerId = selectedLayerId.ToString("D"));
        }

        public Task<IReadOnlyList<TileLayerConfig>> GetCustomLayersAsync()
        {
            return owner.ReadAsync<IReadOnlyList<TileLayerConfig>>(document =>
                document.Maps.GetCustomLayers());
        }

        public Task SetCustomLayersAsync(IReadOnlyList<TileLayerConfig> customLayers)
        {
            return SetAsync(document =>
                document.Maps.SetCustomLayers(customLayers));
        }

        public IObservable<PreferenceValueChange<MapPreferencesValue>> ObserveChanges()
        {
            return Observable
                .FromAsync(async () => new PreferenceValueChange<MapPreferencesValue>(
                    await owner.ReadAsync(document => document.Maps.ToModel()),
                    PreferenceChangeOrigin.SyncApply,
                    AdvancesSyncClock: false))
                .Concat(owner.mapPreferencesSubject)
                .DistinctUntilChanged(MapPreferenceChangeComparer.Instance);
        }

        private async Task SetAsync(Action<AppPreferencesDocument> update)
        {
            await owner.gate.WaitAsync();
            try
            {
                var next = owner.CloneDocument();
                update(next);
                next.Updated = GetCurrentTimestamp();
                await owner.WriteDocumentCoreAsync(next);
                owner.document = next;
                owner.PublishMapPreferenceChange(
                    next.Maps.ToModel(),
                    PreferenceChangeOrigin.LocalWrite,
                    advancesSyncClock: true);
            }
            finally
            {
                owner.gate.Release();
            }
        }

        private sealed class MapPreferenceChangeComparer :
            IEqualityComparer<PreferenceValueChange<MapPreferencesValue>>
        {
            public static readonly MapPreferenceChangeComparer Instance = new();

            public bool Equals(
                PreferenceValueChange<MapPreferencesValue>? previous,
                PreferenceValueChange<MapPreferencesValue>? current)
            {
                if (ReferenceEquals(previous, current))
                {
                    return true;
                }

                if (previous is null || current is null)
                {
                    return false;
                }

                return previous.Origin == current.Origin &&
                    previous.AdvancesSyncClock == current.AdvancesSyncClock &&
                    previous.Value.SelectedLayerId == current.Value.SelectedLayerId &&
                    TileLayerListsEqual(previous.Value.CustomLayers, current.Value.CustomLayers);
            }

            public int GetHashCode(PreferenceValueChange<MapPreferencesValue> change)
            {
                return HashCode.Combine(
                    change.Origin,
                    change.AdvancesSyncClock,
                    change.Value.SelectedLayerId);
            }

            private static bool TileLayerListsEqual(
                IReadOnlyList<TileLayerConfig> previous,
                IReadOnlyList<TileLayerConfig> current)
            {
                if (previous.Count != current.Count)
                {
                    return false;
                }

                for (var i = 0; i < previous.Count; i++)
                {
                    if (!TileLayersEqual(previous[i], current[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool TileLayersEqual(TileLayerConfig previous, TileLayerConfig current)
            {
                return previous.Id == current.Id &&
                    previous.Name == current.Name &&
                    previous.UrlTemplate == current.UrlTemplate &&
                    previous.AttributionText == current.AttributionText &&
                    previous.AttributionUrl == current.AttributionUrl &&
                    previous.MaxZoom == current.MaxZoom &&
                    previous.IsCustom == current.IsCustom;
            }
        }
    }

    private sealed class UiPreferenceStore(AppPreferences owner) : IUiPreferences
    {
        public Task<UiPreferences> GetAsync()
        {
            return owner.ReadAsync(document => document.Ui.ToModel());
        }

        public Task SetLayoutProfileAsync(UiLayoutProfile? layoutProfile)
        {
            return SetAsync(layoutProfile);
        }

        public IObservable<PreferenceValueChange<UiPreferences>> ObserveChanges()
        {
            return Observable
                .FromAsync(async () => new PreferenceValueChange<UiPreferences>(
                    await GetAsync(),
                    PreferenceChangeOrigin.SyncApply,
                    AdvancesSyncClock: false))
                .Concat(owner.uiPreferencesSubject)
                .DistinctUntilChanged();
        }

        private async Task SetAsync(UiLayoutProfile? layoutProfile)
        {
            await owner.gate.WaitAsync();
            try
            {
                var next = owner.CloneDocument();
                next.Ui.LayoutProfile = layoutProfile?.ToString();
                await owner.WriteDocumentCoreAsync(next);
                owner.document = next;
                owner.PublishUiPreferenceChange(
                    next.Ui.ToModel(),
                    PreferenceChangeOrigin.LocalNoSyncClockWrite,
                    advancesSyncClock: false);
            }
            finally
            {
                owner.gate.Release();
            }
        }
    }

    private sealed class RecordedSessionPreferences(AppPreferences owner) : ISessionPreferences
    {
        public Task<SessionPreferences> GetRecordedAsync(Guid sessionId)
        {
            return owner.ReadAsync(document => document.Session.GetRecorded(sessionId));
        }

        public Task<IReadOnlyDictionary<Guid, SessionPreferences>> GetAllRecordedAsync()
        {
            return owner.ReadAsync<IReadOnlyDictionary<Guid, SessionPreferences>>(document =>
                document.Session.ToModelDictionary());
        }

        public async Task UpdateRecordedAsync(Guid sessionId, Func<SessionPreferences, SessionPreferences> update)
        {
            ArgumentNullException.ThrowIfNull(update);

            await owner.gate.WaitAsync();
            try
            {
                var next = owner.CloneDocument();
                var current = next.Session.GetRecorded(sessionId);
                var updated = update(current);
                next.Session.Sessions[SessionKey(sessionId)] = SessionPreferencesDocument.FromModel(updated);
                next.Updated = GetCurrentTimestamp();
                await owner.WriteDocumentCoreAsync(next);
                owner.document = next;
                owner.PublishRecordedPreferenceChanges(
                    next.Session.ToModelDictionary(),
                    PreferenceChangeOrigin.LocalWrite,
                    advancesSyncClock: true,
                    (sessionId, updated));
            }
            finally
            {
                owner.gate.Release();
            }
        }

        public async Task RemoveRecordedAsync(Guid sessionId)
        {
            await owner.gate.WaitAsync();
            try
            {
                var next = owner.CloneDocument();
                next.Session.Sessions.Remove(SessionKey(sessionId));
                next.Updated = GetCurrentTimestamp();
                await owner.WriteDocumentCoreAsync(next);
                owner.document = next;
                owner.PublishRecordedPreferenceChanges(
                    next.Session.ToModelDictionary(),
                    PreferenceChangeOrigin.LocalWrite,
                    advancesSyncClock: true,
                    (sessionId, SessionPreferences.Default));
            }
            finally
            {
                owner.gate.Release();
            }
        }

        public async Task ResetRecordedProcessingToDefaultLocallyAsync(Guid sessionId)
        {
            await owner.gate.WaitAsync();
            try
            {
                var next = owner.CloneDocument();
                // Reset only Processing to the default; the session's other
                // preferences (signal display, analysis, signal layout, layout) are preserved.
                var current = next.Session.GetRecorded(sessionId);
                var reset = current with { Processing = new SessionProcessingPreferences() };
                next.Session.Sessions[SessionKey(sessionId)] = SessionPreferencesDocument.FromModel(reset);
                await owner.WriteDocumentCoreAsync(next);
                owner.document = next;
                owner.PublishRecordedPreferenceChanges(
                    next.Session.ToModelDictionary(),
                    PreferenceChangeOrigin.LocalNoSyncClockWrite,
                    advancesSyncClock: false,
                    (sessionId, reset));
            }
            finally
            {
                owner.gate.Release();
            }
        }

        public IObservable<SessionPreferences> ObserveRecorded(Guid sessionId)
        {
            return ObserveRecordedChanges(sessionId)
                .Select(change => change.Value)
                .DistinctUntilChanged();
        }

        public IObservable<PreferenceValueChange<SessionPreferences>> ObserveRecordedChanges(Guid sessionId)
        {
            return Observable
                .FromAsync(async () => new PreferenceValueChange<SessionPreferences>(
                    await GetRecordedAsync(sessionId),
                    PreferenceChangeOrigin.SyncApply,
                    AdvancesSyncClock: false))
                .Concat(owner.GetRecordedPreferenceSubject(sessionId))
                .DistinctUntilChanged();
        }

        public IObservable<PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>> ObserveAllRecordedChanges()
        {
            return Observable
                .FromAsync(async () => new PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>(
                    await GetAllRecordedAsync(),
                    PreferenceChangeOrigin.SyncApply,
                    AdvancesSyncClock: false))
                .Concat(owner.allRecordedPreferencesSubject)
                .DistinctUntilChanged(AllRecordedPreferenceChangeComparer.Instance);
        }

        private sealed class AllRecordedPreferenceChangeComparer :
            IEqualityComparer<PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>>
        {
            public static readonly AllRecordedPreferenceChangeComparer Instance = new();

            public bool Equals(
                PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>? previous,
                PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>>? current)
            {
                if (ReferenceEquals(previous, current))
                {
                    return true;
                }

                if (previous is null || current is null)
                {
                    return false;
                }

                return previous.Origin == current.Origin &&
                    previous.AdvancesSyncClock == current.AdvancesSyncClock &&
                    RecordedPreferenceDictionariesEqual(previous.Value, current.Value);
            }

            public int GetHashCode(PreferenceValueChange<IReadOnlyDictionary<Guid, SessionPreferences>> change)
            {
                return HashCode.Combine(change.Origin, change.AdvancesSyncClock);
            }

            private static bool RecordedPreferenceDictionariesEqual(
                IReadOnlyDictionary<Guid, SessionPreferences> previous,
                IReadOnlyDictionary<Guid, SessionPreferences> current)
            {
                if (previous.Count != current.Count)
                {
                    return false;
                }

                foreach (var (sessionId, previousPreferences) in previous)
                {
                    if (!current.TryGetValue(sessionId, out var currentPreferences) ||
                        previousPreferences != currentPreferences)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private static string SessionKey(Guid sessionId) => sessionId.ToString("D");
    }

    private sealed class ThemePreferences(AppPreferences owner) : IThemePreferences
    {
        public Task<SufniThemeMode> GetModeAsync()
        {
            return owner.ReadAsync(document => document.Theme.GetMode());
        }

        public Task SetModeAsync(SufniThemeMode mode)
        {
            return SetAsync(mode);
        }

        public IObservable<PreferenceValueChange<SufniThemeMode>> ObserveModeChanges()
        {
            return Observable
                .FromAsync(async () => new PreferenceValueChange<SufniThemeMode>(
                    await GetModeAsync(),
                    PreferenceChangeOrigin.SyncApply,
                    AdvancesSyncClock: false))
                .Concat(owner.themePreferencesSubject)
                .DistinctUntilChanged();
        }

        private async Task SetAsync(SufniThemeMode mode)
        {
            await owner.gate.WaitAsync();
            try
            {
                var next = owner.CloneDocument();
                next.Theme.Mode = mode.ToString();
                next.Updated = GetCurrentTimestamp();
                await owner.WriteDocumentCoreAsync(next);
                owner.document = next;
                owner.PublishThemePreferenceChange(
                    next.Theme.GetMode(),
                    PreferenceChangeOrigin.LocalWrite,
                    advancesSyncClock: true);
            }
            finally
            {
                owner.gate.Release();
            }
        }
    }

    private sealed class AppPreferencesDocument
    {
        public int Version { get; set; } = CurrentVersion;
        public long Updated { get; set; }
        public MapPreferencesDocument Maps { get; set; } = new();
        public SessionPreferencesGroupDocument Session { get; set; } = new();
        public ThemePreferencesDocument Theme { get; set; } = new();
        public UiPreferencesDocument Ui { get; set; } = new();

        public AppPreferencesDocument Normalize(int targetVersion)
        {
            Version = Math.Max(Version, targetVersion);
            Maps ??= new MapPreferencesDocument();
            Maps.CustomLayers ??= [];
            Session ??= new SessionPreferencesGroupDocument();
            Session.Sessions ??= [];
            Session.Normalize();
            Theme ??= new ThemePreferencesDocument();
            Ui ??= new UiPreferencesDocument();
            return this;
        }

        public bool HasUserPreferences()
        {
            return !string.IsNullOrWhiteSpace(Maps.SelectedLayerId)
                || Maps.CustomLayers?.Count > 0
                || Session.Sessions.Count > 0
                || !string.IsNullOrWhiteSpace(Theme.Mode);
        }

        public AppPreferencesSyncData ToSyncData()
        {
            var sessions = Session.Sessions
                .Where(pair => pair.Value is not null && Guid.TryParse(pair.Key, out _))
                .ToDictionary(
                    pair => Guid.Parse(pair.Key),
                    pair => pair.Value!.ToModel());

            return new AppPreferencesSyncData
            {
                Updated = Updated,
                Maps = new MapPreferencesSyncData
                {
                    SelectedLayerId = Guid.TryParse(Maps.SelectedLayerId, out var selectedLayerId)
                        ? selectedLayerId
                        : null,
                    CustomLayers = Maps.GetCustomLayers().ToList(),
                },
                Session = new SessionPreferencesSyncData
                {
                    Sessions = sessions,
                },
                Theme = new ThemePreferencesSyncData
                {
                    Mode = Theme.Mode,
                },
            };
        }

        public void ApplySyncData(AppPreferencesSyncData preferences, int targetVersion)
        {
            var maps = preferences.Maps ?? new MapPreferencesSyncData();
            var session = preferences.Session ?? new SessionPreferencesSyncData();
            var theme = preferences.Theme ?? new ThemePreferencesSyncData();

            Updated = preferences.Updated > 0
                ? preferences.Updated
                : GetCurrentTimestamp();
            Maps = new MapPreferencesDocument
            {
                SelectedLayerId = maps.SelectedLayerId?.ToString("D"),
                CustomLayers = maps.CustomLayers?
                    .Where(layer => layer is not null)
                    .Select(CloneTileLayerConfig)
                    .ToList() ?? [],
            };
            Session = new SessionPreferencesGroupDocument
            {
                Sessions = session.Sessions?
                    .ToDictionary(
                        pair => pair.Key.ToString("D"),
                        pair => (SessionPreferencesDocument?)SessionPreferencesDocument.FromModel(pair.Value))
                    ?? [],
            };
            Theme = new ThemePreferencesDocument
            {
                Mode = theme.Mode,
            };
            Normalize(targetVersion);
        }
    }

    private sealed class UiPreferencesDocument
    {
        public string? LayoutProfile { get; set; }

        public UiPreferences ToModel()
        {
            return new UiPreferences(
                Enum.TryParse<UiLayoutProfile>(LayoutProfile, ignoreCase: false, out var parsed)
                    ? parsed
                    : null);
        }
    }

    private sealed class ThemePreferencesDocument
    {
        public string? Mode { get; set; }

        public SufniThemeMode GetMode()
        {
            return Enum.TryParse<SufniThemeMode>(Mode, ignoreCase: false, out var parsed)
                ? parsed
                : SufniThemeMode.Dark;
        }
    }

    private sealed class MapPreferencesDocument
    {
        public string? SelectedLayerId { get; set; }
        public List<TileLayerConfig>? CustomLayers { get; set; } = [];

        public MapPreferencesValue ToModel()
        {
            return new MapPreferencesValue(
                Guid.TryParse(SelectedLayerId, out var selectedLayerId)
                    ? selectedLayerId
                    : null,
                GetCustomLayers());
        }

        public IReadOnlyList<TileLayerConfig> GetCustomLayers()
        {
            return CustomLayers?
                .Where(layer => layer is not null)
                .Select(CloneTileLayerConfig)
                .ToList() ?? [];
        }

        public void SetCustomLayers(IReadOnlyList<TileLayerConfig> customLayers)
        {
            CustomLayers = customLayers
                .Where(layer => layer is not null)
                .Select(CloneTileLayerConfig)
                .ToList();
        }
    }

    private sealed class SessionPreferencesGroupDocument
    {
        public Dictionary<string, SessionPreferencesDocument?> Sessions { get; set; } = [];

        public void Normalize()
        {
            foreach (var key in Sessions.Keys.ToArray())
            {
                if (Sessions[key] is { } preferences)
                {
                    Sessions[key] = SessionPreferencesDocument.FromModel(preferences.ToModel());
                }
            }
        }

        public SessionPreferences GetRecorded(Guid sessionId)
        {
            return Sessions.TryGetValue(sessionId.ToString("D"), out var preferences) && preferences is not null
                ? preferences.ToModel()
                : SessionPreferences.Default;
        }

        public IReadOnlyDictionary<Guid, SessionPreferences> ToModelDictionary()
        {
            return Sessions
                .Where(pair => pair.Value is not null && Guid.TryParse(pair.Key, out _))
                .ToDictionary(
                    pair => Guid.Parse(pair.Key),
                    pair => pair.Value!.ToModel());
        }
    }

    private sealed class SessionPreferencesDocument
    {
        public SignalDisplayPreferencesDocument? SignalDisplay { get; set; }
        public SignalDisplayPreferencesDocument? Plots { get; set; }
        public AnalysisPreferencesDocument? Analysis { get; set; }
        public AnalysisPreferencesDocument? Statistics { get; set; }
        public SessionProcessingPreferencesDocument? Processing { get; set; }
        public SignalLayoutPreferencesDocument? SignalLayout { get; set; }
        public SignalLayoutPreferencesDocument? Graph { get; set; }
        public SessionLayoutPreferencesDocument? Layout { get; set; }

        public SessionPreferences ToModel()
        {
            return new SessionPreferences(
                signalDisplay: SignalDisplay?.ToModel() ?? Plots?.ToModel() ?? new SignalDisplayPreferences(),
                analysis: Analysis?.ToModel() ?? Statistics?.ToModel() ?? new AnalysisPreferences(),
                processing: Processing?.ToModel() ?? new SessionProcessingPreferences(),
                signalLayout: SignalLayout?.ToModel() ?? Graph?.ToModel() ?? SignalLayoutPreferences.Default,
                layout: Layout?.ToModel() ?? SessionLayoutPreferences.Default);
        }

        public static SessionPreferencesDocument FromModel(SessionPreferences preferences)
        {
            var signalDisplay = SignalDisplayPreferencesDocument.FromModel(preferences.SignalDisplay);
            var analysis = AnalysisPreferencesDocument.FromModel(preferences.Analysis);
            var signalLayout = SignalLayoutPreferencesDocument.FromModel(preferences.SignalLayout);

            return new SessionPreferencesDocument
            {
                SignalDisplay = signalDisplay,
                Analysis = analysis,
                Processing = SessionProcessingPreferencesDocument.FromModel(preferences.Processing),
                SignalLayout = signalLayout,
                Layout = SessionLayoutPreferencesDocument.FromModel(preferences.Layout),
            };
        }
    }

    private sealed class SignalDisplayPreferencesDocument
    {
        public bool? Travel { get; set; }
        public bool? Velocity { get; set; }
        public bool? Imu { get; set; }
        public bool? PitchRoll { get; set; }
        public bool? Speed { get; set; }
        public bool? Elevation { get; set; }
        public string? TravelSmoothing { get; set; }
        public string? VelocitySmoothing { get; set; }
        public string? ImuSmoothing { get; set; }
        public string? PitchRollSmoothing { get; set; }
        public string? SpeedSmoothing { get; set; }
        public string? ElevationSmoothing { get; set; }

        public SignalDisplayPreferences ToModel()
        {
            return new SignalDisplayPreferences(
                Travel: Travel ?? true,
                Velocity: Velocity ?? true,
                Imu: Imu ?? true,
                PitchRoll: PitchRoll ?? true,
                TravelSmoothing: ParseEnum(TravelSmoothing, PlotSmoothingLevel.Off),
                VelocitySmoothing: ParseEnum(VelocitySmoothing, PlotSmoothingLevel.Off),
                ImuSmoothing: ParseEnum(ImuSmoothing, PlotSmoothingLevel.Off),
                PitchRollSmoothing: ParseEnum(PitchRollSmoothing, PlotSmoothingLevel.Off),
                Speed: Speed ?? true,
                Elevation: Elevation ?? true,
                SpeedSmoothing: ParseEnum(SpeedSmoothing, PlotSmoothingLevel.Off),
                ElevationSmoothing: ParseEnum(ElevationSmoothing, PlotSmoothingLevel.Off));
        }

        public static SignalDisplayPreferencesDocument FromModel(SignalDisplayPreferences preferences)
        {
            return new SignalDisplayPreferencesDocument
            {
                Travel = preferences.Travel,
                Velocity = preferences.Velocity,
                Imu = preferences.Imu,
                PitchRoll = preferences.PitchRoll,
                Speed = preferences.Speed,
                Elevation = preferences.Elevation,
                TravelSmoothing = preferences.TravelSmoothing.ToString(),
                VelocitySmoothing = preferences.VelocitySmoothing.ToString(),
                ImuSmoothing = preferences.ImuSmoothing.ToString(),
                PitchRollSmoothing = preferences.PitchRollSmoothing.ToString(),
                SpeedSmoothing = preferences.SpeedSmoothing.ToString(),
                ElevationSmoothing = preferences.ElevationSmoothing.ToString(),
            };
        }

        private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
            where TEnum : struct, Enum
        {
            return Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed)
                ? parsed
                : fallback;
        }
    }

    private sealed class AnalysisPreferencesDocument
    {
        public string? TravelDistributionMode { get; set; }
        public string? TravelHistogramMode { get; set; }
        public string? VelocityAverageMode { get; set; }
        public string? BalanceDisplacementMode { get; set; }
        public string? BalanceSpeedMode { get; set; }
        public string? SessionInsightsTargetProfile { get; set; }
        public string? SessionAnalysisTargetProfile { get; set; }

        public AnalysisPreferences ToModel()
        {
            return new AnalysisPreferences(
                ParseEnum(TravelDistributionMode ?? TravelHistogramMode, Sufni.Telemetry.TravelDistributionMode.ActiveSuspension),
                ParseEnum(VelocityAverageMode, Sufni.Telemetry.VelocityAverageMode.SampleAveraged),
                ParseEnum(BalanceDisplacementMode, Sufni.Telemetry.BalanceDisplacementMode.Zenith),
                ParseEnum(BalanceSpeedMode, Sufni.Telemetry.BalanceSpeedMode.Both),
                ParseEnum(SessionInsightsTargetProfile ?? SessionAnalysisTargetProfile, Sufni.App.Sessions.Models.SessionInsightsTargetProfile.Trail));
        }

        public static AnalysisPreferencesDocument FromModel(AnalysisPreferences preferences)
        {
            return new AnalysisPreferencesDocument
            {
                TravelDistributionMode = preferences.TravelDistributionMode.ToString(),
                VelocityAverageMode = preferences.VelocityAverageMode.ToString(),
                BalanceDisplacementMode = preferences.BalanceDisplacementMode.ToString(),
                BalanceSpeedMode = preferences.BalanceSpeedMode.ToString(),
                SessionInsightsTargetProfile = preferences.SessionInsightsTargetProfile.ToString(),
            };
        }

        private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
            where TEnum : struct, Enum
        {
            return Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed)
                ? parsed
                : fallback;
        }
    }

    private sealed class SessionProcessingPreferencesDocument
    {
        public int? VelocityFilterWindowMilliseconds { get; set; }

        public SessionProcessingPreferences ToModel()
        {
            return new SessionProcessingPreferences(
                VelocityFilterWindowMilliseconds ??
                TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds);
        }

        public static SessionProcessingPreferencesDocument FromModel(SessionProcessingPreferences preferences)
        {
            return new SessionProcessingPreferencesDocument
            {
                VelocityFilterWindowMilliseconds = preferences.VelocityFilterWindowMilliseconds,
            };
        }
    }

    private sealed class SignalLayoutPreferencesDocument
    {
        public List<SignalLayoutRowPreferencesDocument?>? Rows { get; set; }

        public SignalLayoutPreferences ToModel()
        {
            var rows = Rows?
                .Where(row => row is not null)
                .Select(row => row!.ToModel())
                .Where(row => !string.IsNullOrWhiteSpace(row.RowId))
                .ToArray();

            return rows is { Length: > 0 }
                ? new SignalLayoutPreferences(rows)
                : SignalLayoutPreferences.Default;
        }

        public static SignalLayoutPreferencesDocument FromModel(SignalLayoutPreferences preferences)
        {
            return new SignalLayoutPreferencesDocument
            {
                Rows = preferences.Rows
                    .Select(row => (SignalLayoutRowPreferencesDocument?)SignalLayoutRowPreferencesDocument.FromModel(row))
                    .ToList(),
            };
        }
    }

    private sealed class SignalLayoutRowPreferencesDocument
    {
        public string? RowId { get; set; }
        public bool? IsExpanded { get; set; }
        public double? HeightRatio { get; set; }
        public List<SignalLayoutRowPreferencesDocument?>? Children { get; set; }

        public SignalLayoutRowPreferences ToModel()
        {
            return new SignalLayoutRowPreferences(
                RowId ?? "",
                IsExpanded ?? true,
                Children?
                    .Where(child => child is not null)
                    .Select(child => child!.ToModel())
                    .Where(child => !string.IsNullOrWhiteSpace(child.RowId))
                    .ToArray(),
                HeightRatio);
        }

        public static SignalLayoutRowPreferencesDocument FromModel(SignalLayoutRowPreferences preferences)
        {
            return new SignalLayoutRowPreferencesDocument
            {
                RowId = preferences.RowId,
                IsExpanded = preferences.IsExpanded,
                HeightRatio = preferences.HeightRatio,
                Children = preferences.Children
                    .Select(child => (SignalLayoutRowPreferencesDocument?)FromModel(child))
                    .ToList(),
            };
        }
    }

    private sealed class SessionLayoutPreferencesDocument
    {
        public SessionPaneGroupPreferencesDocument? DesktopShellRows { get; set; }
        public SessionPaneGroupPreferencesDocument? DesktopSignalsMediaColumns { get; set; }
        public SessionPaneGroupPreferencesDocument? DesktopGraphMediaColumns { get; set; }
        public SessionPaneGroupPreferencesDocument? DesktopAnalysisSidebarColumns { get; set; }
        public SessionPaneGroupPreferencesDocument? DesktopStatisticsSidebarColumns { get; set; }
        public SessionPaneGroupPreferencesDocument? DesktopMediaRows { get; set; }

        public SessionLayoutPreferences ToModel()
        {
            return new SessionLayoutPreferences(
                DesktopShellRows?.ToModel(),
                DesktopSignalsMediaColumns?.ToModel() ?? DesktopGraphMediaColumns?.ToModel(),
                DesktopAnalysisSidebarColumns?.ToModel() ?? DesktopStatisticsSidebarColumns?.ToModel(),
                DesktopMediaRows?.ToModel());
        }

        public static SessionLayoutPreferencesDocument FromModel(SessionLayoutPreferences preferences)
        {
            var desktopSignalsMediaColumns = SessionPaneGroupPreferencesDocument.FromModel(
                preferences.DesktopSignalsMediaColumns);
            var desktopAnalysisSidebarColumns = SessionPaneGroupPreferencesDocument.FromModel(
                preferences.DesktopAnalysisSidebarColumns);

            return new SessionLayoutPreferencesDocument
            {
                DesktopShellRows = SessionPaneGroupPreferencesDocument.FromModel(
                    preferences.DesktopShellRows),
                DesktopSignalsMediaColumns = desktopSignalsMediaColumns,
                DesktopAnalysisSidebarColumns = desktopAnalysisSidebarColumns,
                DesktopMediaRows = SessionPaneGroupPreferencesDocument.FromModel(
                    preferences.DesktopMediaRows),
            };
        }
    }

    private sealed class SessionPaneGroupPreferencesDocument
    {
        public List<SessionPaneSizePreferenceDocument?>? Panes { get; set; }

        public SessionPaneGroupPreferences ToModel()
        {
            return new SessionPaneGroupPreferences(Panes?
                .Where(pane => pane is not null)
                .Select(pane => pane!.ToModel())
                .Where(pane => !string.IsNullOrWhiteSpace(pane.PaneId))
                .ToArray());
        }

        public static SessionPaneGroupPreferencesDocument? FromModel(SessionPaneGroupPreferences? preferences)
        {
            if (preferences is null)
            {
                return null;
            }

            return new SessionPaneGroupPreferencesDocument
            {
                Panes = preferences.Panes
                    .Select(pane => (SessionPaneSizePreferenceDocument?)SessionPaneSizePreferenceDocument.FromModel(pane))
                    .ToList(),
            };
        }
    }

    private sealed class SessionPaneSizePreferenceDocument
    {
        public string? PaneId { get; set; }
        public double? Ratio { get; set; }
        public bool? IsCollapsed { get; set; }

        public SessionPaneSizePreference ToModel()
        {
            return new SessionPaneSizePreference(PaneId ?? "", Ratio ?? 0, IsCollapsed ?? false);
        }

        public static SessionPaneSizePreferenceDocument FromModel(SessionPaneSizePreference preferences)
        {
            return new SessionPaneSizePreferenceDocument
            {
                PaneId = SessionLayoutPaneIds.Normalize(preferences.PaneId),
                Ratio = preferences.Ratio,
                IsCollapsed = preferences.IsCollapsed,
            };
        }
    }
}

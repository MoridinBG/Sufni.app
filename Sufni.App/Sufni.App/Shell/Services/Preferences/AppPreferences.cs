using Sufni.App.MapsAndTracks.Services;
using Sufni.App.Sessions.Models;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Services.Preferences;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using System.Reactive.Subjects;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.Telemetry;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Infrastructure.Theming;

namespace Sufni.App.Shell.Services.Preferences;

public sealed class AppPreferences : IAppPreferences
{
    internal const int CurrentVersion = 4;
    private static readonly ILogger logger = Log.ForContext<AppPreferences>();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };
    private readonly string filePath;
    private readonly IBackgroundTaskRunner background;
    private readonly IUiThreadDispatcher dispatcher;
    private readonly SemaphoreSlim gate = new(1, 1);
    private AppPreferencesDocument document;
    private readonly Subject<Guid?> mapChanges = new();
    private readonly object watchGate = new();
    private readonly Dictionary<Guid, RecordedPreferenceWatch> recordedWatches = [];

    public IMapPreferences Map { get; }
    public ISessionPreferences Session { get; }
    public IThemePreferences Theme { get; }
    public IUiPreferences Ui { get; }

    public AppPreferences(string filePath, LaunchPreferences launchPreferences,
        IBackgroundTaskRunner background, IUiThreadDispatcher dispatcher)
    {
        this.filePath = filePath;
        this.background = background;
        this.dispatcher = dispatcher;
        document = launchPreferences.Document;
        Map = new MapPreferences(this);
        Session = new RecordedSessionPreferences(this);
        Theme = new ThemePreferences(this);
        Ui = new UiPreferenceStore(this);
    }

    public static LaunchPreferences ReadLaunchValues(string filePath, UiLayoutProfile defaultLayoutProfile) =>
        new(ReadDocumentCore(filePath), defaultLayoutProfile);

    // Startup calls this after SQLite has retained the captured legacy custom layers.
    public Task InitializeAsync() => UpdateAsync(_ => { }, _ => { });

    private async Task<TResult> ReadAsync<TResult>(Func<AppPreferencesDocument, TResult> read)
    {
        await gate.WaitAsync();
        try { return read(document); }
        finally { gate.Release(); }
    }

    private async Task UpdateAsync(Action<AppPreferencesDocument> update, Action<AppPreferencesDocument> publish)
    {
        await gate.WaitAsync();
        try
        {
            var current = document;
            var next = await background.RunAsync(() =>
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(current, JsonOptions);
                return JsonSerializer.Deserialize<AppPreferencesDocument>(bytes, JsonOptions)!.Normalize(CurrentVersion);
            });
            update(next);
            await background.RunAsync(() => WriteDocumentAsync(next));
            await dispatcher.InvokeAsync(() =>
            {
                document = next;
                publish(next);
            });
        }
        finally { gate.Release(); }
    }

    private IObservable<Guid?> ObserveMap() => Observable.Create<Guid?>(async observer =>
    {
        await gate.WaitAsync();
        try
        {
            IDisposable subscription = Disposable.Empty;
            await dispatcher.InvokeAsync(() =>
            {
                subscription = mapChanges.Subscribe(observer);
                observer.OnNext(document.Maps.GetSelectedId());
            });
            return subscription;
        }
        finally { gate.Release(); }
    }).DistinctUntilChanged();

    private IObservable<SessionPreferences> ObserveRecorded(Guid id) => Observable.Create<SessionPreferences>(async observer =>
    {
        await gate.WaitAsync();
        try
        {
            RecordedPreferenceWatch? watch = null;
            IDisposable subscription = Disposable.Empty;
            await dispatcher.InvokeAsync(() =>
            {
                lock (watchGate)
                {
                    if (!recordedWatches.TryGetValue(id, out watch))
                        recordedWatches.Add(id, watch = new RecordedPreferenceWatch());
                    watch.Subscribers++;
                    subscription = watch.Subject.Subscribe(observer);
                    observer.OnNext(document.Session.GetRecorded(id));
                }
            });
            return Disposable.Create(() =>
            {
                subscription.Dispose();
                lock (watchGate)
                {
                    if (--watch!.Subscribers == 0)
                    {
                        recordedWatches.Remove(id);
                        watch.Subject.Dispose();
                    }
                }
            });
        }
        finally { gate.Release(); }
    }).DistinctUntilChanged();

    private void PublishRecorded(Guid id, SessionPreferences value)
    {
        lock (watchGate)
            if (recordedWatches.TryGetValue(id, out var watch)) watch.Subject.OnNext(value);
    }

    private sealed class RecordedPreferenceWatch
    {
        public Subject<SessionPreferences> Subject { get; } = new();
        public int Subscribers;
    }

    private static AppPreferencesDocument ReadDocumentCore(string filePath)
    {
        if (!File.Exists(filePath)) return new AppPreferencesDocument();
        try
        {
            using var stream = File.OpenRead(filePath);
            return JsonSerializer.Deserialize<AppPreferencesDocument>(stream, JsonOptions) ?? new AppPreferencesDocument();
        }
        catch (JsonException exception)
        {
            logger.Warning(exception, "Failed to read app preferences JSON at {PreferencesPath}; using defaults", filePath);
            return new AppPreferencesDocument();
        }
    }

    private async Task WriteDocumentAsync(AppPreferencesDocument next)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(next.Normalize(CurrentVersion), JsonOptions);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory ?? string.Empty, $".{Path.GetFileName(filePath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, bytes);
            File.Move(temp, filePath, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private sealed class MapPreferences(AppPreferences owner) : IMapPreferences
    {
        public Task<Guid?> GetSelectedLayerIdAsync() => owner.ReadAsync(document => document.Maps.GetSelectedId());
        public Task SetSelectedLayerIdAsync(Guid id) => owner.UpdateAsync(
            document => document.Maps.SelectedLayerId = id.ToString("D"),
            document => owner.mapChanges.OnNext(document.Maps.GetSelectedId()));
        public IObservable<Guid?> ObserveChanges() => owner.ObserveMap();
    }

    private sealed class UiPreferenceStore(AppPreferences owner) : IUiPreferences
    {
        public Task SetLayoutProfileAsync(UiLayoutProfile? profile) => owner.UpdateAsync(
            document => document.Ui.LayoutProfile = profile?.ToString(), _ => { });
    }

    private sealed class ThemePreferences(AppPreferences owner) : IThemePreferences
    {
        public Task<SufniThemeMode> GetModeAsync() => owner.ReadAsync(document => document.Theme.GetMode());
        public Task SetModeAsync(SufniThemeMode mode) => owner.UpdateAsync(
            document => document.Theme.Mode = mode.ToString(), _ => { });
    }

    private sealed class RecordedSessionPreferences(AppPreferences owner) : ISessionPreferences
    {
        public Task<SessionPreferences> GetRecordedAsync(Guid id) => owner.ReadAsync(document => document.Session.GetRecorded(id));
        public Task UpdateRecordedAsync(Guid id, Func<SessionPreferences, SessionPreferences> update) => owner.UpdateAsync(
            document => document.Session.Sessions[id.ToString("D")] = SessionPreferencesDocument.FromModel(update(document.Session.GetRecorded(id))),
            document => owner.PublishRecorded(id, document.Session.GetRecorded(id)));
        public Task RemoveRecordedAsync(Guid id) => owner.UpdateAsync(
            document => document.Session.Sessions.Remove(id.ToString("D")),
            document => owner.PublishRecorded(id, document.Session.GetRecorded(id)));
        public IObservable<SessionPreferences> ObserveRecorded(Guid id) => owner.ObserveRecorded(id);
    }

    internal sealed class AppPreferencesDocument
    {
        public int Version { get; set; } = CurrentVersion;
        [JsonPropertyName("updated")]
        public long? LegacyUpdated { get; set; }
        public MapPreferencesDocument Maps { get; set; } = new();
        public SessionPreferencesGroupDocument Session { get; set; } = new();
        public ThemePreferencesDocument Theme { get; set; } = new();
        public UiPreferencesDocument Ui { get; set; } = new();

        public AppPreferencesDocument Normalize(int targetVersion)
        {
            Version = Math.Max(Version, targetVersion);
            LegacyUpdated = null;
            Maps ??= new MapPreferencesDocument();
            Maps.CustomLayers = null;
            Session ??= new SessionPreferencesGroupDocument();
            Session.Sessions ??= [];
            Session.Normalize();
            Theme ??= new ThemePreferencesDocument();
            Ui ??= new UiPreferencesDocument();
            return this;
        }
    }

    internal sealed class UiPreferencesDocument
    {
        public string? LayoutProfile { get; set; }

        public UiLayoutProfile? GetLayoutProfile() =>
            Enum.TryParse<UiLayoutProfile>(LayoutProfile, ignoreCase: false, out var parsed)
                ? parsed
                : null;
    }

    internal sealed class ThemePreferencesDocument
    {
        public string? Mode { get; set; }

        public SufniThemeMode GetMode()
        {
            return Enum.TryParse<SufniThemeMode>(Mode, ignoreCase: false, out var parsed)
                ? parsed
                : SufniThemeMode.Dark;
        }
    }

    internal sealed class MapPreferencesDocument
    {
        public string? SelectedLayerId { get; set; }
        public List<TileLayerConfig>? CustomLayers { get; set; }
        public Guid? GetSelectedId() => Guid.TryParse(SelectedLayerId, out var id) ? id : null;
    }

}

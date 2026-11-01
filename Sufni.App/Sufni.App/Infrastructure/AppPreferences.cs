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

namespace Sufni.App.Infrastructure;

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

    internal sealed class SessionPreferencesGroupDocument
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

    internal sealed class SessionPreferencesDocument
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
                SignalLayout = signalLayout,
                Layout = SessionLayoutPreferencesDocument.FromModel(preferences.Layout),
            };
        }
    }

    internal sealed class SignalDisplayPreferencesDocument
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

    internal sealed class AnalysisPreferencesDocument
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

    internal sealed class SessionProcessingPreferencesDocument
    {
        public int? VelocityFilterWindowMilliseconds { get; set; }

    }

    internal sealed class SignalLayoutPreferencesDocument
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

    internal sealed class SignalLayoutRowPreferencesDocument
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

    internal sealed class SessionLayoutPreferencesDocument
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

    internal sealed class SessionPaneGroupPreferencesDocument
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

    internal sealed class SessionPaneSizePreferenceDocument
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

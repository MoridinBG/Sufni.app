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

    // Hot stream of "remote sync just landed in the on-disk document". Fired
    // after the gate is released so subscribers can re-read without contending
    // for it. Initial-value-less: subscribers care about future emissions, not
    // a snapshot of "has sync ever happened".
    private readonly Subject<Unit> syncDataAppliedSubject = new();

    public IMapPreferences Map { get; }
    public ISessionPreferences Session { get; }
    public IThemePreferences Theme { get; }
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
        Map = new MapPreferences(this);
        Session = new RecordedSessionPreferences(this);
        Theme = new ThemePreferences(this);
    }

    private async Task<TResult> ReadAsync<TResult>(Func<AppPreferencesDocument, TResult> read)
    {
        await gate.WaitAsync();
        try
        {
            var document = await ReadDocumentCoreAsync();
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
            var document = await ReadDocumentCoreAsync();
            if (document.Updated <= 0 && document.HasUserPreferences())
            {
                document.Updated = GetCurrentTimestamp();
                await WriteDocumentCoreAsync(document);
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
        bool applied = false;
        await gate.WaitAsync();
        try
        {
            var document = await ReadDocumentCoreAsync();
            if (preferences.Updated < document.Updated)
            {
                return;
            }

            document.ApplySyncData(preferences, documentVersion);
            await WriteDocumentCoreAsync(document);
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
            var document = await ReadDocumentCoreAsync();
            update(document);
            document.Updated = GetCurrentTimestamp();
            await WriteDocumentCoreAsync(document);
        }
        finally
        {
            gate.Release();
        }
    }

    // Persists a change WITHOUT advancing the document's sync clock. A no-bump
    // write keeps the document from winning whole-document last-writer-wins sync,
    // so a purely local normalization cannot overwrite a peer's unrelated
    // preferences. Use only for changes that every device derives identically.
    private async Task UpdateWithoutAdvancingSyncClockAsync(Action<AppPreferencesDocument> update)
    {
        await gate.WaitAsync();
        try
        {
            var document = await ReadDocumentCoreAsync();
            update(document);
            await WriteDocumentCoreAsync(document);
        }
        finally
        {
            gate.Release();
        }
    }

    private static long GetCurrentTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private async Task<AppPreferencesDocument> ReadDocumentCoreAsync()
    {
        if (!File.Exists(filePath))
        {
            return new AppPreferencesDocument().Normalize(documentVersion);
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            var document = await JsonSerializer.DeserializeAsync<AppPreferencesDocument>(stream, JsonOptions);
            return document?.Normalize(documentVersion) ?? new AppPreferencesDocument().Normalize(documentVersion);
        }
        catch (JsonException ex)
        {
            logger.Warning(ex, "Failed to read app preferences JSON at {PreferencesPath}; using defaults", filePath);
            return new AppPreferencesDocument();
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
            return owner.UpdateAsync(document =>
                document.Maps.SelectedLayerId = selectedLayerId.ToString("D"));
        }

        public Task<IReadOnlyList<TileLayerConfig>> GetCustomLayersAsync()
        {
            return owner.ReadAsync<IReadOnlyList<TileLayerConfig>>(document =>
                document.Maps.CustomLayers?.Where(layer => layer is not null).ToList() ?? []);
        }

        public Task SetCustomLayersAsync(IReadOnlyList<TileLayerConfig> customLayers)
        {
            return owner.UpdateAsync(document =>
                document.Maps.CustomLayers = customLayers.ToList());
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
            {
                var result = new Dictionary<Guid, SessionPreferences>();
                foreach (var (key, value) in document.Session.Sessions)
                {
                    if (value is not null && Guid.TryParse(key, out var sessionId))
                    {
                        result[sessionId] = value.ToModel();
                    }
                }

                return result;
            });
        }

        public Task UpdateRecordedAsync(Guid sessionId, Func<SessionPreferences, SessionPreferences> update)
        {
            ArgumentNullException.ThrowIfNull(update);

            return owner.UpdateAsync(document =>
            {
                var current = document.Session.GetRecorded(sessionId);
                document.Session.Sessions[SessionKey(sessionId)] = SessionPreferencesDocument.FromModel(update(current));
            });
        }

        public Task RemoveRecordedAsync(Guid sessionId)
        {
            return owner.UpdateAsync(document => document.Session.Sessions.Remove(SessionKey(sessionId)));
        }

        public Task ResetRecordedProcessingToDefaultLocallyAsync(Guid sessionId)
        {
            return owner.UpdateWithoutAdvancingSyncClockAsync(document =>
            {
                // Reset only Processing to the default; the session's other
                // preferences (signal display, analysis, signal layout, layout) are preserved.
                var current = document.Session.GetRecorded(sessionId);
                var reset = current with { Processing = new SessionProcessingPreferences() };
                document.Session.Sessions[SessionKey(sessionId)] = SessionPreferencesDocument.FromModel(reset);
            });
        }

        public IObservable<SessionPreferences> ObserveRecorded(Guid sessionId)
        {
            return owner.SyncDataApplied
                .SelectMany(_ => Observable.FromAsync(() => GetRecordedAsync(sessionId)))
                .DistinctUntilChanged();
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
            return owner.UpdateAsync(document => document.Theme.Mode = mode.ToString());
        }
    }

    private sealed class AppPreferencesDocument
    {
        public int Version { get; set; } = CurrentVersion;
        public long Updated { get; set; }
        public MapPreferencesDocument Maps { get; set; } = new();
        public SessionPreferencesGroupDocument Session { get; set; } = new();
        public ThemePreferencesDocument Theme { get; set; } = new();

        public AppPreferencesDocument Normalize(int targetVersion)
        {
            Version = Math.Max(Version, targetVersion);
            Maps ??= new MapPreferencesDocument();
            Maps.CustomLayers ??= [];
            Session ??= new SessionPreferencesGroupDocument();
            Session.Sessions ??= [];
            Session.Normalize();
            Theme ??= new ThemePreferencesDocument();
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
                    CustomLayers = Maps.CustomLayers?.Where(layer => layer is not null).ToList() ?? [],
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
                CustomLayers = maps.CustomLayers?.Where(layer => layer is not null).ToList() ?? [],
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

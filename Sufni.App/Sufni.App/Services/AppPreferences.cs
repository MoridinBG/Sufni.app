using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.Services;

public sealed class AppPreferences : IAppPreferences
{
    private const int CurrentVersion = 1;
    private static readonly ILogger logger = Log.ForContext<AppPreferences>();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly string filePath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public IMapPreferences Map { get; }
    public ISessionPreferences Session { get; }

    public AppPreferences()
        : this(Path.Combine(Path.GetDirectoryName(AppPaths.DatabasePath)!, "app-preferences.json"))
    {
    }

    internal AppPreferences(string filePath)
    {
        this.filePath = filePath;
        Map = new MapPreferences(this);
        Session = new RecordedSessionPreferences(this);
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

    private async Task UpdateAsync(Action<AppPreferencesDocument> update)
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

    private async Task<AppPreferencesDocument> ReadDocumentCoreAsync()
    {
        if (!File.Exists(filePath))
        {
            return new AppPreferencesDocument();
        }

        try
        {
            await using var stream = File.OpenRead(filePath);
            var document = await JsonSerializer.DeserializeAsync<AppPreferencesDocument>(stream, JsonOptions);
            return document?.Normalize() ?? new AppPreferencesDocument();
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
                await JsonSerializer.SerializeAsync(stream, document.Normalize(), JsonOptions);
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

        private static string SessionKey(Guid sessionId) => sessionId.ToString("D");
    }

    private sealed class AppPreferencesDocument
    {
        public int Version { get; set; } = CurrentVersion;
        public MapPreferencesDocument Maps { get; set; } = new();
        public SessionPreferencesGroupDocument Session { get; set; } = new();

        public AppPreferencesDocument Normalize()
        {
            Version = CurrentVersion;
            Maps ??= new MapPreferencesDocument();
            Maps.CustomLayers ??= [];
            Session ??= new SessionPreferencesGroupDocument();
            Session.Sessions ??= [];
            return this;
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

        public SessionPreferences GetRecorded(Guid sessionId)
        {
            return Sessions.TryGetValue(sessionId.ToString("D"), out var preferences) && preferences is not null
                ? preferences.ToModel()
                : SessionPreferences.Default;
        }
    }

    private sealed class SessionPreferencesDocument
    {
        public SessionPlotPreferencesDocument? Plots { get; set; }
        public SessionStatisticsPreferencesDocument? Statistics { get; set; }

        public SessionPreferences ToModel()
        {
            return new SessionPreferences(
                Plots?.ToModel() ?? new SessionPlotPreferences(),
                Statistics?.ToModel() ?? new SessionStatisticsPreferences());
        }

        public static SessionPreferencesDocument FromModel(SessionPreferences preferences)
        {
            return new SessionPreferencesDocument
            {
                Plots = SessionPlotPreferencesDocument.FromModel(preferences.Plots),
                Statistics = SessionStatisticsPreferencesDocument.FromModel(preferences.Statistics),
            };
        }
    }

    private sealed class SessionPlotPreferencesDocument
    {
        public bool? Travel { get; set; }
        public bool? Velocity { get; set; }
        public bool? Imu { get; set; }
        public bool? Speed { get; set; }
        public bool? Elevation { get; set; }
        public string? TravelSmoothing { get; set; }
        public string? VelocitySmoothing { get; set; }
        public string? ImuSmoothing { get; set; }
        public string? SpeedSmoothing { get; set; }
        public string? ElevationSmoothing { get; set; }

        public SessionPlotPreferences ToModel()
        {
            return new SessionPlotPreferences(
                Travel: Travel ?? true,
                Velocity: Velocity ?? true,
                Imu: Imu ?? true,
                TravelSmoothing: ParseEnum(TravelSmoothing, PlotSmoothingLevel.Off),
                VelocitySmoothing: ParseEnum(VelocitySmoothing, PlotSmoothingLevel.Off),
                ImuSmoothing: ParseEnum(ImuSmoothing, PlotSmoothingLevel.Off),
                Speed: Speed ?? true,
                Elevation: Elevation ?? true,
                SpeedSmoothing: ParseEnum(SpeedSmoothing, PlotSmoothingLevel.Off),
                ElevationSmoothing: ParseEnum(ElevationSmoothing, PlotSmoothingLevel.Off));
        }

        public static SessionPlotPreferencesDocument FromModel(SessionPlotPreferences preferences)
        {
            return new SessionPlotPreferencesDocument
            {
                Travel = preferences.Travel,
                Velocity = preferences.Velocity,
                Imu = preferences.Imu,
                Speed = preferences.Speed,
                Elevation = preferences.Elevation,
                TravelSmoothing = preferences.TravelSmoothing.ToString(),
                VelocitySmoothing = preferences.VelocitySmoothing.ToString(),
                ImuSmoothing = preferences.ImuSmoothing.ToString(),
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

    private sealed class SessionStatisticsPreferencesDocument
    {
        public string? TravelHistogramMode { get; set; }
        public string? VelocityAverageMode { get; set; }
        public string? BalanceDisplacementMode { get; set; }
        public string? SessionAnalysisTargetProfile { get; set; }

        public SessionStatisticsPreferences ToModel()
        {
            return new SessionStatisticsPreferences(
                ParseEnum(TravelHistogramMode, Sufni.Telemetry.TravelHistogramMode.ActiveSuspension),
                ParseEnum(VelocityAverageMode, Sufni.Telemetry.VelocityAverageMode.SampleAveraged),
                ParseEnum(BalanceDisplacementMode, Sufni.Telemetry.BalanceDisplacementMode.Zenith),
                ParseEnum(SessionAnalysisTargetProfile, Sufni.App.Models.SessionAnalysisTargetProfile.Trail));
        }

        public static SessionStatisticsPreferencesDocument FromModel(SessionStatisticsPreferences preferences)
        {
            return new SessionStatisticsPreferencesDocument
            {
                TravelHistogramMode = preferences.TravelHistogramMode.ToString(),
                VelocityAverageMode = preferences.VelocityAverageMode.ToString(),
                BalanceDisplacementMode = preferences.BalanceDisplacementMode.ToString(),
                SessionAnalysisTargetProfile = preferences.SessionAnalysisTargetProfile.ToString(),
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
}

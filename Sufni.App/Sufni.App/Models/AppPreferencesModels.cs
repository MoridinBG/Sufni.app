using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Sufni.Telemetry;

namespace Sufni.App.Models;

public enum PlotSmoothingLevel
{
    Off,
    Light,
    Strong,
}

public sealed record SessionPreferences
{
    public SessionPreferences()
    {
    }

    public SessionPreferences(
        SessionPlotPreferences? plots = null,
        SessionStatisticsPreferences? statistics = null,
        SessionProcessingPreferences? processing = null,
        SessionGraphPreferences? graph = null)
    {
        Plots = plots ?? new SessionPlotPreferences();
        Statistics = statistics ?? new SessionStatisticsPreferences();
        Processing = processing ?? new SessionProcessingPreferences();
        Graph = graph ?? SessionGraphPreferences.Default;
    }

    [JsonPropertyName("plots")]
    public SessionPlotPreferences Plots { get; init; } = new();

    [JsonPropertyName("statistics")]
    public SessionStatisticsPreferences Statistics { get; init; } = new();

    [JsonPropertyName("processing")]
    public SessionProcessingPreferences Processing { get; init; } = new();

    [JsonPropertyName("graph")]
    public SessionGraphPreferences Graph { get; init; } = SessionGraphPreferences.Default;

    public static SessionPreferences Default => new();
}

public sealed record SessionPlotPreferences(
    [property: JsonPropertyName("travel")] bool Travel = true,
    [property: JsonPropertyName("velocity")] bool Velocity = true,
    [property: JsonPropertyName("imu")] bool Imu = true,
    [property: JsonPropertyName("travel_smoothing")] PlotSmoothingLevel TravelSmoothing = PlotSmoothingLevel.Off,
    [property: JsonPropertyName("velocity_smoothing")] PlotSmoothingLevel VelocitySmoothing = PlotSmoothingLevel.Off,
    [property: JsonPropertyName("imu_smoothing")] PlotSmoothingLevel ImuSmoothing = PlotSmoothingLevel.Off,
    [property: JsonPropertyName("speed")] bool Speed = true,
    [property: JsonPropertyName("elevation")] bool Elevation = true,
    [property: JsonPropertyName("speed_smoothing")] PlotSmoothingLevel SpeedSmoothing = PlotSmoothingLevel.Off,
    [property: JsonPropertyName("elevation_smoothing")] PlotSmoothingLevel ElevationSmoothing = PlotSmoothingLevel.Off);

public sealed record SessionStatisticsPreferences(
    [property: JsonPropertyName("travel_histogram_mode")] TravelHistogramMode TravelHistogramMode = TravelHistogramMode.ActiveSuspension,
    [property: JsonPropertyName("velocity_average_mode")] VelocityAverageMode VelocityAverageMode = VelocityAverageMode.SampleAveraged,
    [property: JsonPropertyName("balance_displacement_mode")] BalanceDisplacementMode BalanceDisplacementMode = BalanceDisplacementMode.Zenith,
    [property: JsonPropertyName("balance_speed_mode")] BalanceSpeedMode BalanceSpeedMode = BalanceSpeedMode.Both,
    [property: JsonPropertyName("session_analysis_target_profile")] SessionAnalysisTargetProfile SessionAnalysisTargetProfile = SessionAnalysisTargetProfile.Trail);

public sealed record SessionProcessingPreferences(
    [property: JsonPropertyName("velocity_filter_window_ms")] int VelocityFilterWindowMilliseconds = TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds)
{
    public TelemetryProcessingOptions ToTelemetryProcessingOptions()
    {
        return new TelemetryProcessingOptions(VelocityFilterWindowMilliseconds);
    }
}

public sealed record SessionGraphPreferences
{
    public SessionGraphPreferences()
        : this(CreateDefaultRows())
    {
    }

    public SessionGraphPreferences(IReadOnlyList<SessionGraphRowPreferences>? rows)
    {
        Rows = rows?.ToArray() ?? CreateDefaultRows();
    }

    [JsonPropertyName("rows")]
    public IReadOnlyList<SessionGraphRowPreferences> Rows { get; init; } = CreateDefaultRows();

    public static SessionGraphPreferences Default => new();

    public bool Equals(SessionGraphPreferences? other)
    {
        return other is not null && Rows.SequenceEqual(other.Rows);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var row in Rows)
        {
            hash.Add(row);
        }

        return hash.ToHashCode();
    }

    public static IReadOnlyList<SessionGraphRowPreferences> CreateDefaultRows()
    {
        return
        [
            new SessionGraphRowPreferences(
                TelemetryGraphRowIds.Travel,
                children:
                [
                    new SessionGraphRowPreferences(TelemetryGraphRowIds.Velocity),
                ]),
            new SessionGraphRowPreferences(TelemetryGraphRowIds.Imu),
            new SessionGraphRowPreferences(
                TelemetryGraphRowIds.Speed,
                children:
                [
                    new SessionGraphRowPreferences(TelemetryGraphRowIds.Elevation),
                ]),
        ];
    }
}

public sealed record SessionGraphRowPreferences
{
    public SessionGraphRowPreferences()
    {
    }

    public SessionGraphRowPreferences(
        string rowId,
        bool isExpanded = true,
        IReadOnlyList<SessionGraphRowPreferences>? children = null)
    {
        RowId = rowId;
        IsExpanded = isExpanded;
        Children = children?.ToArray() ?? [];
    }

    [JsonPropertyName("row_id")]
    public string RowId { get; init; } = "";

    [JsonPropertyName("is_expanded")]
    public bool IsExpanded { get; init; } = true;

    [JsonPropertyName("children")]
    public IReadOnlyList<SessionGraphRowPreferences> Children { get; init; } = [];

    public bool Equals(SessionGraphRowPreferences? other)
    {
        return other is not null &&
               RowId == other.RowId &&
               IsExpanded == other.IsExpanded &&
               Children.SequenceEqual(other.Children);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RowId);
        hash.Add(IsExpanded);
        foreach (var child in Children)
        {
            hash.Add(child);
        }

        return hash.ToHashCode();
    }
}

public static class TelemetryGraphRowIds
{
    public const string Travel = "travel";
    public const string Velocity = "velocity";
    public const string Imu = "imu";
    public const string Speed = "speed";
    public const string Elevation = "elevation";
}

public sealed class AppPreferencesSyncData
{
    [JsonPropertyName("updated")]
    public long Updated { get; set; }

    [JsonPropertyName("maps")]
    public MapPreferencesSyncData Maps { get; set; } = new();

    [JsonPropertyName("session")]
    public SessionPreferencesSyncData Session { get; set; } = new();
}

public sealed class MapPreferencesSyncData
{
    [JsonPropertyName("selected_layer_id")]
    public Guid? SelectedLayerId { get; set; }

    [JsonPropertyName("custom_layers")]
    public List<TileLayerConfig> CustomLayers { get; set; } = [];
}

public sealed class SessionPreferencesSyncData
{
    [JsonPropertyName("sessions")]
    public Dictionary<Guid, SessionPreferences> Sessions { get; set; } = [];
}

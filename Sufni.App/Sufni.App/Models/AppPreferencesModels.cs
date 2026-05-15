using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using Sufni.Telemetry;

namespace Sufni.App.Models;

public enum PlotSmoothingLevel
{
    Off,
    Light,
    Strong,
}

public sealed record SessionPreferences(
    [property: JsonPropertyName("plots")] SessionPlotPreferences Plots,
    [property: JsonPropertyName("statistics")] SessionStatisticsPreferences Statistics)
{
    public static SessionPreferences Default => new(new SessionPlotPreferences(), new SessionStatisticsPreferences());
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

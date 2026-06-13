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
        SessionGraphPreferences? graph = null,
        SessionLayoutPreferences? layout = null)
    {
        Plots = plots ?? new SessionPlotPreferences();
        Statistics = statistics ?? new SessionStatisticsPreferences();
        Processing = processing ?? new SessionProcessingPreferences();
        Graph = graph ?? SessionGraphPreferences.Default;
        Layout = layout ?? SessionLayoutPreferences.Default;
    }

    [JsonPropertyName("plots")]
    public SessionPlotPreferences Plots { get; init; } = new();

    [JsonPropertyName("statistics")]
    public SessionStatisticsPreferences Statistics { get; init; } = new();

    [JsonPropertyName("processing")]
    public SessionProcessingPreferences Processing { get; init; } = new();

    [JsonPropertyName("graph")]
    public SessionGraphPreferences Graph { get; init; } = SessionGraphPreferences.Default;

    [JsonPropertyName("layout")]
    public SessionLayoutPreferences Layout { get; init; } = SessionLayoutPreferences.Default;

    public static SessionPreferences Default => new();
}

public sealed record SessionPlotPreferences(
    [property: JsonPropertyName("travel")] bool Travel = true,
    [property: JsonPropertyName("velocity")] bool Velocity = true,
    [property: JsonPropertyName("imu")] bool Imu = true,
    [property: JsonPropertyName("pitch_roll")] bool PitchRoll = true,
    [property: JsonPropertyName("travel_smoothing")] PlotSmoothingLevel TravelSmoothing = PlotSmoothingLevel.Off,
    [property: JsonPropertyName("velocity_smoothing")] PlotSmoothingLevel VelocitySmoothing = PlotSmoothingLevel.Off,
    [property: JsonPropertyName("imu_smoothing")] PlotSmoothingLevel ImuSmoothing = PlotSmoothingLevel.Off,
    [property: JsonPropertyName("pitch_roll_smoothing")] PlotSmoothingLevel PitchRollSmoothing = PlotSmoothingLevel.Off,
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
            new SessionGraphRowPreferences(
                TelemetryGraphRowIds.Imu,
                children:
                [
                    new SessionGraphRowPreferences(TelemetryGraphRowIds.PitchRoll),
                ]),
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
        IReadOnlyList<SessionGraphRowPreferences>? children = null,
        double? heightRatio = null)
    {
        RowId = rowId;
        IsExpanded = isExpanded;
        Children = children?.ToArray() ?? [];
        HeightRatio = IsValidRatio(heightRatio) ? heightRatio : null;
    }

    [JsonPropertyName("row_id")]
    public string RowId { get; init; } = "";

    [JsonPropertyName("is_expanded")]
    public bool IsExpanded { get; init; } = true;

    [JsonPropertyName("children")]
    public IReadOnlyList<SessionGraphRowPreferences> Children { get; init; } = [];

    [JsonPropertyName("height_ratio")]
    public double? HeightRatio { get; init; }

    public bool Equals(SessionGraphRowPreferences? other)
    {
        return other is not null &&
               RowId == other.RowId &&
               IsExpanded == other.IsExpanded &&
               HeightRatio == other.HeightRatio &&
               Children.SequenceEqual(other.Children);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(RowId);
        hash.Add(IsExpanded);
        hash.Add(HeightRatio);
        foreach (var child in Children)
        {
            hash.Add(child);
        }

        return hash.ToHashCode();
    }

    private static bool IsValidRatio(double? ratio) =>
        ratio is { } value && double.IsFinite(value) && value > 0;
}

public sealed record SessionLayoutPreferences
{
    public SessionLayoutPreferences()
    {
    }

    public SessionLayoutPreferences(
        SessionPaneGroupPreferences? desktopShellRows = null,
        SessionPaneGroupPreferences? desktopGraphMediaColumns = null,
        SessionPaneGroupPreferences? desktopStatisticsSidebarColumns = null,
        SessionPaneGroupPreferences? desktopMediaRows = null)
    {
        DesktopShellRows = desktopShellRows;
        DesktopGraphMediaColumns = desktopGraphMediaColumns;
        DesktopStatisticsSidebarColumns = desktopStatisticsSidebarColumns;
        DesktopMediaRows = desktopMediaRows;
    }

    [JsonPropertyName("desktop_shell_rows")]
    public SessionPaneGroupPreferences? DesktopShellRows { get; init; }

    [JsonPropertyName("desktop_graph_media_columns")]
    public SessionPaneGroupPreferences? DesktopGraphMediaColumns { get; init; }

    [JsonPropertyName("desktop_statistics_sidebar_columns")]
    public SessionPaneGroupPreferences? DesktopStatisticsSidebarColumns { get; init; }

    [JsonPropertyName("desktop_media_rows")]
    public SessionPaneGroupPreferences? DesktopMediaRows { get; init; }

    public static SessionLayoutPreferences Default => new();
}

public sealed record SessionPaneGroupPreferences
{
    public SessionPaneGroupPreferences()
    {
    }

    public SessionPaneGroupPreferences(IReadOnlyList<SessionPaneSizePreference>? panes)
    {
        Panes = panes?.ToArray() ?? [];
    }

    [JsonPropertyName("panes")]
    public IReadOnlyList<SessionPaneSizePreference> Panes { get; init; } = [];

    public bool TryGetPaneStates(
        IReadOnlyList<string> paneIds,
        out IReadOnlyList<SessionPaneStatePreference> states)
    {
        states = [];
        if (paneIds.Count == 0 || Panes.Count != paneIds.Count)
        {
            return false;
        }

        var statesById = new Dictionary<string, SessionPaneStatePreference>(StringComparer.Ordinal);
        foreach (var pane in Panes)
        {
            if (string.IsNullOrWhiteSpace(pane.PaneId) ||
                !double.IsFinite(pane.Ratio) ||
                pane.Ratio <= 0 ||
                !statesById.TryAdd(
                    pane.PaneId,
                    new SessionPaneStatePreference(pane.PaneId, pane.Ratio, pane.IsCollapsed)))
            {
                return false;
            }
        }

        var values = new SessionPaneStatePreference[paneIds.Count];
        for (var i = 0; i < paneIds.Count; i++)
        {
            if (!statesById.TryGetValue(paneIds[i], out var state))
            {
                return false;
            }

            values[i] = state;
        }

        states = values;
        return true;
    }

    public bool TryGetRatios(IReadOnlyList<string> paneIds, out IReadOnlyList<double> ratios)
    {
        ratios = [];
        if (!TryGetPaneStates(paneIds, out var states))
        {
            return false;
        }

        ratios = states.Select(static state => state.Ratio).ToArray();
        return true;
    }

    public bool Equals(SessionPaneGroupPreferences? other)
    {
        return other is not null && Panes.SequenceEqual(other.Panes);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var pane in Panes)
        {
            hash.Add(pane);
        }

        return hash.ToHashCode();
    }
}

public sealed record SessionPaneStatePreference(
    string PaneId,
    double Ratio,
    bool IsCollapsed);

public sealed record SessionPaneSizePreference(
    [property: JsonPropertyName("pane_id")] string PaneId,
    [property: JsonPropertyName("ratio")] double Ratio,
    [property: JsonPropertyName("is_collapsed")] bool IsCollapsed = false);

public static class SessionLayoutPaneIds
{
    public const string Graph = "graph";
    public const string Media = "media";
    public const string Map = "map";
    public const string ExtensionMedia = "extension_media";
    public const string GraphMediaArea = "graph_media_area";
    public const string StatisticsSidebarArea = "statistics_sidebar_area";
    public const string Statistics = "statistics";
    public const string Sidebar = "sidebar";
}

public static class TelemetryGraphRowIds
{
    public const string Travel = "travel";
    public const string Velocity = "velocity";
    public const string Imu = "imu";
    public const string PitchRoll = "pitch_roll";
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

    [JsonPropertyName("theme")]
    public ThemePreferencesSyncData Theme { get; set; } = new();
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

public sealed class ThemePreferencesSyncData
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Sufni.Telemetry;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Infrastructure;

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
        SignalDisplayPreferences? signalDisplay = null,
        AnalysisPreferences? analysis = null,
        SessionProcessingPreferences? processing = null,
        SignalLayoutPreferences? signalLayout = null,
        SessionLayoutPreferences? layout = null)
    {
        SignalDisplay = signalDisplay ?? new SignalDisplayPreferences();
        Analysis = analysis ?? new AnalysisPreferences();
        Processing = processing ?? new SessionProcessingPreferences();
        SignalLayout = signalLayout ?? SignalLayoutPreferences.Default;
        Layout = layout ?? SessionLayoutPreferences.Default;
    }

    [JsonPropertyName("signal_display")]
    public SignalDisplayPreferences SignalDisplay { get; init; } = new();

    [JsonPropertyName("analysis")]
    public AnalysisPreferences Analysis { get; init; } = new();

    [JsonPropertyName("processing")]
    public SessionProcessingPreferences Processing { get; init; } = new();

    [JsonPropertyName("signal_layout")]
    public SignalLayoutPreferences SignalLayout { get; init; } = SignalLayoutPreferences.Default;

    [JsonPropertyName("layout")]
    public SessionLayoutPreferences Layout { get; init; } = SessionLayoutPreferences.Default;

    public static SessionPreferences Default => new();
}

public sealed record SignalDisplayPreferences(
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

public sealed record AnalysisPreferences(
    [property: JsonPropertyName("travel_distribution_mode")] TravelDistributionMode TravelDistributionMode = TravelDistributionMode.ActiveSuspension,
    [property: JsonPropertyName("velocity_average_mode")] VelocityAverageMode VelocityAverageMode = VelocityAverageMode.SampleAveraged,
    [property: JsonPropertyName("balance_displacement_mode")] BalanceDisplacementMode BalanceDisplacementMode = BalanceDisplacementMode.Zenith,
    [property: JsonPropertyName("balance_speed_mode")] BalanceSpeedMode BalanceSpeedMode = BalanceSpeedMode.Both,
    [property: JsonPropertyName("session_insights_target_profile")] SessionInsightsTargetProfile SessionInsightsTargetProfile = SessionInsightsTargetProfile.Trail);

public sealed record SessionProcessingPreferences(
    [property: JsonPropertyName("velocity_filter_window_ms")] int VelocityFilterWindowMilliseconds = TelemetryProcessingOptions.DefaultVelocityFilterWindowMilliseconds)
{
    public TelemetryProcessingOptions ToTelemetryProcessingOptions()
    {
        return new TelemetryProcessingOptions(VelocityFilterWindowMilliseconds);
    }
}

public sealed record SignalLayoutPreferences
{
    public SignalLayoutPreferences()
        : this(CreateDefaultRows())
    {
    }

    public SignalLayoutPreferences(IReadOnlyList<SignalLayoutRowPreferences>? rows)
    {
        Rows = rows?.ToArray() ?? CreateDefaultRows();
    }

    [JsonPropertyName("rows")]
    public IReadOnlyList<SignalLayoutRowPreferences> Rows { get; init; } = CreateDefaultRows();

    public static SignalLayoutPreferences Default => new();

    public bool Equals(SignalLayoutPreferences? other)
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

    public static IReadOnlyList<SignalLayoutRowPreferences> CreateDefaultRows()
    {
        return
        [
            new SignalLayoutRowPreferences(
                SignalRowIds.Travel,
                children:
                [
                    new SignalLayoutRowPreferences(SignalRowIds.Velocity),
                ]),
            new SignalLayoutRowPreferences(
                SignalRowIds.Imu,
                children:
                [
                    new SignalLayoutRowPreferences(SignalRowIds.PitchRoll),
                ]),
            new SignalLayoutRowPreferences(
                SignalRowIds.Speed,
                children:
                [
                    new SignalLayoutRowPreferences(SignalRowIds.Elevation),
                ]),
        ];
    }
}

public sealed record SignalLayoutRowPreferences
{
    public SignalLayoutRowPreferences()
    {
    }

    public SignalLayoutRowPreferences(
        string rowId,
        bool isExpanded = true,
        IReadOnlyList<SignalLayoutRowPreferences>? children = null,
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
    public IReadOnlyList<SignalLayoutRowPreferences> Children { get; init; } = [];

    [JsonPropertyName("height_ratio")]
    public double? HeightRatio { get; init; }

    public bool Equals(SignalLayoutRowPreferences? other)
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
        SessionPaneGroupPreferences? desktopSignalsMediaColumns = null,
        SessionPaneGroupPreferences? desktopAnalysisSidebarColumns = null,
        SessionPaneGroupPreferences? desktopMediaRows = null)
    {
        DesktopShellRows = desktopShellRows;
        DesktopSignalsMediaColumns = desktopSignalsMediaColumns;
        DesktopAnalysisSidebarColumns = desktopAnalysisSidebarColumns;
        DesktopMediaRows = desktopMediaRows;
    }

    [JsonPropertyName("desktop_shell_rows")]
    public SessionPaneGroupPreferences? DesktopShellRows { get; init; }

    [JsonPropertyName("desktop_signals_media_columns")]
    public SessionPaneGroupPreferences? DesktopSignalsMediaColumns { get; init; }

    [JsonPropertyName("desktop_analysis_sidebar_columns")]
    public SessionPaneGroupPreferences? DesktopAnalysisSidebarColumns { get; init; }

    [JsonPropertyName("desktop_media_rows")]
    public SessionPaneGroupPreferences? DesktopMediaRows { get; init; }

    public static SessionLayoutPreferences Default => new();
}

public sealed record SessionPaneGroupPreferences
{
    public SessionPaneGroupPreferences()
    {
    }

    [JsonConstructor]
    public SessionPaneGroupPreferences(IReadOnlyList<SessionPaneSizePreference>? panes)
    {
        Panes = NormalizePanes(panes);
    }

    [JsonPropertyName("panes")]
    public IReadOnlyList<SessionPaneSizePreference> Panes { get; init; } = [];

    public bool TryGetPaneStates(
        IReadOnlyList<string> paneIds,
        out IReadOnlyList<SessionPaneStatePreference> states)
    {
        states = [];
        var requestedPaneIds = paneIds.Select(SessionLayoutPaneIds.Normalize).ToArray();
        if (requestedPaneIds.Length == 0 || Panes.Count != requestedPaneIds.Length)
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

        var values = new SessionPaneStatePreference[requestedPaneIds.Length];
        for (var i = 0; i < requestedPaneIds.Length; i++)
        {
            if (!statesById.TryGetValue(requestedPaneIds[i], out var state))
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

    private static IReadOnlyList<SessionPaneSizePreference> NormalizePanes(IReadOnlyList<SessionPaneSizePreference>? panes)
    {
        if (panes is null || panes.Count == 0)
        {
            return [];
        }

        var byPaneId = new Dictionary<string, (SessionPaneSizePreference Pane, bool IsLegacy, int Index)>(StringComparer.Ordinal);
        for (var i = 0; i < panes.Count; i++)
        {
            var pane = panes[i];
            var normalizedPaneId = SessionLayoutPaneIds.Normalize(pane.PaneId);
            var normalizedPane = pane with { PaneId = normalizedPaneId };
            var isLegacy = SessionLayoutPaneIds.IsLegacy(pane.PaneId);

            if (!byPaneId.TryGetValue(normalizedPaneId, out var current) ||
                (current.IsLegacy && !isLegacy) ||
                current.IsLegacy == isLegacy)
            {
                byPaneId[normalizedPaneId] = (normalizedPane, isLegacy, i);
            }
        }

        return byPaneId.Values
            .OrderBy(value => value.Index)
            .Select(value => value.Pane)
            .ToArray();
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
    public const string Signals = "signals";
    public const string Media = "media";
    public const string Map = "map";
    public const string ExtensionMedia = "extension_media";
    public const string SignalsMediaArea = "signals_media_area";
    public const string AnalysisSidebarArea = "analysis_sidebar_area";
    public const string Analysis = "analysis";
    public const string Sidebar = "sidebar";

    internal const string LegacyGraph = "graph";
    internal const string LegacyGraphMediaArea = "graph_media_area";
    internal const string LegacyAnalysisSidebarArea = "statistics_sidebar_area";
    internal const string LegacyAnalysis = "statistics";

    internal static string Normalize(string? paneId)
    {
        return paneId switch
        {
            LegacyGraph => Signals,
            LegacyGraphMediaArea => SignalsMediaArea,
            LegacyAnalysisSidebarArea => AnalysisSidebarArea,
            LegacyAnalysis => Analysis,
            null => "",
            _ => paneId,
        };
    }

    internal static string ToLegacy(string? paneId)
    {
        return Normalize(paneId) switch
        {
            Signals => LegacyGraph,
            SignalsMediaArea => LegacyGraphMediaArea,
            AnalysisSidebarArea => LegacyAnalysisSidebarArea,
            Analysis => LegacyAnalysis,
            var value => value,
        };
    }

    internal static bool IsLegacy(string? paneId)
    {
        return paneId is LegacyGraph or LegacyGraphMediaArea or LegacyAnalysisSidebarArea or LegacyAnalysis;
    }
}

public static class SignalRowIds
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

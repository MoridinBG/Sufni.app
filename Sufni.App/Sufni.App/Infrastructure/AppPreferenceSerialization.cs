using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sufni.Telemetry;

using Sufni.App.Sessions.Models;

namespace Sufni.App.Infrastructure;

internal static class AppPreferenceSerialization
{
    public const int NewPreferenceKeysVersion = 3;
    public const int NewLayoutPaneIdsVersion = 3;
    public const int CurrentVersion = NewPreferenceKeysVersion;

    public static bool ShouldWriteLegacyPreferenceKeys(int version) => version < NewPreferenceKeysVersion;

    public static bool ShouldWriteLegacyLayoutPaneIds(int version) => version < NewLayoutPaneIdsVersion;
}

internal sealed class SessionPreferencesJsonConverter(int version) : JsonConverter<SessionPreferences>
{
    public override SessionPreferences Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        SignalDisplayPreferences? legacySignalDisplay = null;
        SignalDisplayPreferences? signalDisplay = null;
        AnalysisPreferences? legacyAnalysis = null;
        AnalysisPreferences? analysis = null;
        SessionProcessingPreferences? processing = null;
        SignalLayoutPreferences? legacySignalLayout = null;
        SignalLayoutPreferences? signalLayout = null;
        SessionLayoutPreferences? layout = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new SessionPreferences(
                    signalDisplay: signalDisplay ?? legacySignalDisplay,
                    analysis: analysis ?? legacyAnalysis,
                    processing: processing,
                    signalLayout: signalLayout ?? legacySignalLayout,
                    layout: layout);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }

            var propertyName = reader.GetString();
            reader.Read();
            switch (propertyName)
            {
                case "plots":
                    legacySignalDisplay = ReadNullable<SignalDisplayPreferences>(ref reader, options);
                    break;
                case "signal_display":
                    signalDisplay = ReadNullable<SignalDisplayPreferences>(ref reader, options);
                    break;
                case "statistics":
                    legacyAnalysis = ReadNullable<AnalysisPreferences>(ref reader, options);
                    break;
                case "analysis":
                    analysis = ReadNullable<AnalysisPreferences>(ref reader, options);
                    break;
                case "processing":
                    processing = ReadNullable<SessionProcessingPreferences>(ref reader, options);
                    break;
                case "graph":
                    legacySignalLayout = ReadNullable<SignalLayoutPreferences>(ref reader, options);
                    break;
                case "signal_layout":
                    signalLayout = ReadNullable<SignalLayoutPreferences>(ref reader, options);
                    break;
                case "layout":
                    layout = ReadNullable<SessionLayoutPreferences>(ref reader, options);
                    break;
                default:
                    SkipOrThrow(ref reader, options, propertyName);
                    break;
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, SessionPreferences value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (AppPreferenceSerialization.ShouldWriteLegacyPreferenceKeys(version))
        {
            writer.WritePropertyName("plots");
            JsonSerializer.Serialize(writer, value.SignalDisplay, options);
        }

        writer.WritePropertyName("signal_display");
        JsonSerializer.Serialize(writer, value.SignalDisplay, options);

        if (AppPreferenceSerialization.ShouldWriteLegacyPreferenceKeys(version))
        {
            writer.WritePropertyName("statistics");
            JsonSerializer.Serialize(writer, value.Analysis, options);
        }

        writer.WritePropertyName("analysis");
        JsonSerializer.Serialize(writer, value.Analysis, options);

        writer.WritePropertyName("processing");
        JsonSerializer.Serialize(writer, value.Processing, options);

        if (AppPreferenceSerialization.ShouldWriteLegacyPreferenceKeys(version))
        {
            writer.WritePropertyName("graph");
            JsonSerializer.Serialize(writer, value.SignalLayout, options);
        }

        writer.WritePropertyName("signal_layout");
        JsonSerializer.Serialize(writer, value.SignalLayout, options);

        writer.WritePropertyName("layout");
        JsonSerializer.Serialize(writer, value.Layout, options);
        writer.WriteEndObject();
    }

    private static T? ReadNullable<T>(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.Null
            ? default
            : JsonSerializer.Deserialize<T>(ref reader, options);
    }

    private static void SkipOrThrow(ref Utf8JsonReader reader, JsonSerializerOptions options, string? propertyName)
    {
        if (options.UnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow)
        {
            throw new JsonException($"Unknown JSON property '{propertyName}'.");
        }

        reader.Skip();
    }
}

internal sealed class AnalysisPreferencesJsonConverter(int version) : JsonConverter<AnalysisPreferences>
{
    public override AnalysisPreferences Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        TravelDistributionMode? legacyTravelDistributionMode = null;
        TravelDistributionMode? travelDistributionMode = null;
        VelocityAverageMode? velocityAverageMode = null;
        BalanceDisplacementMode? balanceDisplacementMode = null;
        BalanceSpeedMode? balanceSpeedMode = null;
        SessionInsightsTargetProfile? legacyTargetProfile = null;
        SessionInsightsTargetProfile? targetProfile = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new AnalysisPreferences(
                    travelDistributionMode ?? legacyTravelDistributionMode ?? TravelDistributionMode.ActiveSuspension,
                    velocityAverageMode ?? VelocityAverageMode.SampleAveraged,
                    balanceDisplacementMode ?? BalanceDisplacementMode.Zenith,
                    balanceSpeedMode ?? BalanceSpeedMode.Both,
                    targetProfile ?? legacyTargetProfile ?? SessionInsightsTargetProfile.Trail);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }

            var propertyName = reader.GetString();
            reader.Read();
            switch (propertyName)
            {
                case "travel_histogram_mode":
                    legacyTravelDistributionMode = ReadEnum<TravelDistributionMode>(ref reader, options);
                    break;
                case "travel_distribution_mode":
                    travelDistributionMode = ReadEnum<TravelDistributionMode>(ref reader, options);
                    break;
                case "velocity_average_mode":
                    velocityAverageMode = ReadEnum<VelocityAverageMode>(ref reader, options);
                    break;
                case "balance_displacement_mode":
                    balanceDisplacementMode = ReadEnum<BalanceDisplacementMode>(ref reader, options);
                    break;
                case "balance_speed_mode":
                    balanceSpeedMode = ReadEnum<BalanceSpeedMode>(ref reader, options);
                    break;
                case "session_analysis_target_profile":
                    legacyTargetProfile = ReadEnum<SessionInsightsTargetProfile>(ref reader, options);
                    break;
                case "session_insights_target_profile":
                    targetProfile = ReadEnum<SessionInsightsTargetProfile>(ref reader, options);
                    break;
                default:
                    SkipOrThrow(ref reader, options, propertyName);
                    break;
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, AnalysisPreferences value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (AppPreferenceSerialization.ShouldWriteLegacyPreferenceKeys(version))
        {
            writer.WritePropertyName("travel_histogram_mode");
            JsonSerializer.Serialize(writer, value.TravelDistributionMode, options);
        }

        writer.WritePropertyName("travel_distribution_mode");
        JsonSerializer.Serialize(writer, value.TravelDistributionMode, options);

        writer.WritePropertyName("velocity_average_mode");
        JsonSerializer.Serialize(writer, value.VelocityAverageMode, options);

        writer.WritePropertyName("balance_displacement_mode");
        JsonSerializer.Serialize(writer, value.BalanceDisplacementMode, options);

        writer.WritePropertyName("balance_speed_mode");
        JsonSerializer.Serialize(writer, value.BalanceSpeedMode, options);

        if (AppPreferenceSerialization.ShouldWriteLegacyPreferenceKeys(version))
        {
            writer.WritePropertyName("session_analysis_target_profile");
            JsonSerializer.Serialize(writer, value.SessionInsightsTargetProfile, options);
        }

        writer.WritePropertyName("session_insights_target_profile");
        JsonSerializer.Serialize(writer, value.SessionInsightsTargetProfile, options);
        writer.WriteEndObject();
    }

    private static TEnum? ReadEnum<TEnum>(ref Utf8JsonReader reader, JsonSerializerOptions options)
        where TEnum : struct, Enum
    {
        return reader.TokenType == JsonTokenType.Null
            ? null
            : JsonSerializer.Deserialize<TEnum>(ref reader, options);
    }

    private static void SkipOrThrow(ref Utf8JsonReader reader, JsonSerializerOptions options, string? propertyName)
    {
        if (options.UnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow)
        {
            throw new JsonException($"Unknown JSON property '{propertyName}'.");
        }

        reader.Skip();
    }
}

internal sealed class SessionLayoutPreferencesJsonConverter(int version) : JsonConverter<SessionLayoutPreferences>
{
    public override SessionLayoutPreferences Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        SessionPaneGroupPreferences? desktopShellRows = null;
        SessionPaneGroupPreferences? legacyDesktopSignalsMediaColumns = null;
        SessionPaneGroupPreferences? desktopSignalsMediaColumns = null;
        SessionPaneGroupPreferences? legacyDesktopAnalysisSidebarColumns = null;
        SessionPaneGroupPreferences? desktopAnalysisSidebarColumns = null;
        SessionPaneGroupPreferences? desktopMediaRows = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new SessionLayoutPreferences(
                    desktopShellRows,
                    desktopSignalsMediaColumns ?? legacyDesktopSignalsMediaColumns,
                    desktopAnalysisSidebarColumns ?? legacyDesktopAnalysisSidebarColumns,
                    desktopMediaRows);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }

            var propertyName = reader.GetString();
            reader.Read();
            switch (propertyName)
            {
                case "desktop_shell_rows":
                    desktopShellRows = ReadNullable<SessionPaneGroupPreferences>(ref reader, options);
                    break;
                case "desktop_graph_media_columns":
                    legacyDesktopSignalsMediaColumns = ReadNullable<SessionPaneGroupPreferences>(ref reader, options);
                    break;
                case "desktop_signals_media_columns":
                    desktopSignalsMediaColumns = ReadNullable<SessionPaneGroupPreferences>(ref reader, options);
                    break;
                case "desktop_statistics_sidebar_columns":
                    legacyDesktopAnalysisSidebarColumns = ReadNullable<SessionPaneGroupPreferences>(ref reader, options);
                    break;
                case "desktop_analysis_sidebar_columns":
                    desktopAnalysisSidebarColumns = ReadNullable<SessionPaneGroupPreferences>(ref reader, options);
                    break;
                case "desktop_media_rows":
                    desktopMediaRows = ReadNullable<SessionPaneGroupPreferences>(ref reader, options);
                    break;
                default:
                    SkipOrThrow(ref reader, options, propertyName);
                    break;
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, SessionLayoutPreferences value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("desktop_shell_rows");
        JsonSerializer.Serialize(writer, value.DesktopShellRows, options);

        if (AppPreferenceSerialization.ShouldWriteLegacyPreferenceKeys(version))
        {
            writer.WritePropertyName("desktop_graph_media_columns");
            JsonSerializer.Serialize(writer, value.DesktopSignalsMediaColumns, options);
        }

        writer.WritePropertyName("desktop_signals_media_columns");
        JsonSerializer.Serialize(writer, value.DesktopSignalsMediaColumns, options);

        if (AppPreferenceSerialization.ShouldWriteLegacyPreferenceKeys(version))
        {
            writer.WritePropertyName("desktop_statistics_sidebar_columns");
            JsonSerializer.Serialize(writer, value.DesktopAnalysisSidebarColumns, options);
        }

        writer.WritePropertyName("desktop_analysis_sidebar_columns");
        JsonSerializer.Serialize(writer, value.DesktopAnalysisSidebarColumns, options);

        writer.WritePropertyName("desktop_media_rows");
        JsonSerializer.Serialize(writer, value.DesktopMediaRows, options);
        writer.WriteEndObject();
    }

    private static T? ReadNullable<T>(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        return reader.TokenType == JsonTokenType.Null
            ? default
            : JsonSerializer.Deserialize<T>(ref reader, options);
    }

    private static void SkipOrThrow(ref Utf8JsonReader reader, JsonSerializerOptions options, string? propertyName)
    {
        if (options.UnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow)
        {
            throw new JsonException($"Unknown JSON property '{propertyName}'.");
        }

        reader.Skip();
    }
}

internal sealed class SessionPaneSizePreferenceJsonConverter(int version) : JsonConverter<SessionPaneSizePreference>
{
    public override SessionPaneSizePreference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException();
        }

        string? paneId = null;
        double? ratio = null;
        bool? isCollapsed = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new SessionPaneSizePreference(paneId ?? "", ratio ?? 0, isCollapsed ?? false);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }

            var propertyName = reader.GetString();
            reader.Read();
            switch (propertyName)
            {
                case "pane_id":
                    paneId = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();
                    break;
                case "ratio":
                    ratio = reader.TokenType == JsonTokenType.Null ? null : reader.GetDouble();
                    break;
                case "is_collapsed":
                    isCollapsed = reader.TokenType == JsonTokenType.Null ? null : reader.GetBoolean();
                    break;
                default:
                    if (options.UnmappedMemberHandling == JsonUnmappedMemberHandling.Disallow)
                    {
                        throw new JsonException($"Unknown JSON property '{propertyName}'.");
                    }

                    reader.Skip();
                    break;
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, SessionPaneSizePreference value, JsonSerializerOptions options)
    {
        var paneId = AppPreferenceSerialization.ShouldWriteLegacyLayoutPaneIds(version)
            ? SessionLayoutPaneIds.ToLegacy(value.PaneId)
            : SessionLayoutPaneIds.Normalize(value.PaneId);

        writer.WriteStartObject();
        writer.WriteString("pane_id", paneId);
        writer.WriteNumber("ratio", value.Ratio);
        writer.WriteBoolean("is_collapsed", value.IsCollapsed);
        writer.WriteEndObject();
    }
}

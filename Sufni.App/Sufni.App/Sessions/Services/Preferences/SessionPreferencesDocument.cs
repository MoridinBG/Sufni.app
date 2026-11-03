using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.Sessions.Models;
using Sufni.App.Shared.Plots;

namespace Sufni.App.Sessions.Services.Preferences;

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

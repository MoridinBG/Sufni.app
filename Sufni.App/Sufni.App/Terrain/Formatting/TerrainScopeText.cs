using System;
using System.Linq;
using Sufni.App.Shared.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Formatting;

internal static class TerrainScopeText
{
    public static string Name(TerrainPreset preset) => preset switch
    {
        TerrainPreset.Descents => "Descents",
        TerrainPreset.Climbs => "Climbs",
        _ => "All"
    };

    /// <summary>The preset and its toggled classes, for example "Descents · Rough, Very rough".</summary>
    public static string Label(TerrainScope scope)
    {
        var classes = Enum.GetValues<GradeBand>().Where(scope.Filter.Contains).Select(TerrainClassText.Name)
            .Concat(Enum.GetValues<RoughnessClass>().Where(scope.Filter.Contains).Select(TerrainClassText.Name)).ToArray();
        return classes.Length == 0 ? Name(scope.Preset) : $"{Name(scope.Preset)} · {string.Join(", ", classes)}";
    }

    public static string Banner(TerrainScope scope, TerrainSelectionResult selection)
    {
        var text = $"{Label(scope)} · {UnitsFormatter.FormatDistance(selection.SelectedMetres)}";
        return selection.StoppedSeconds > 0
            ? $"{text} · {UnitsFormatter.FormatDuration(TimeSpan.FromSeconds(selection.StoppedSeconds))} stopped excluded"
            : text;
    }

    public static string Empty(TerrainScope scope) => $"No {Label(scope)} ground in this time range.";

    public static string? NoGrade(TerrainGradeState state) => state switch
    {
        TerrainGradeState.Available => null,
        TerrainGradeState.NoFrameSensor => "No grade without a frame sensor.",
        TerrainGradeState.NoElevation => "No grade: the track has no elevation.",
        _ => "No grade: the ride is too short."
    };
}

using System;
using System.Collections.Generic;
using Sufni.App.Shared.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Formatting;

internal static class ClimbingReports
{
    public static ClimbingReport? Build(ClassifiedProfile classified, TelemetryTimeRange? range)
    {
        var profile = classified.Profile;
        if (TerrainClimbing.Describe(classified, range) is not { } climbing) return null;
        var bob = $"{climbing.BobMillimetres:0} mm";
        if (profile.RearMaxTravel is > 0 and var rearTravel) bob += $" · {climbing.BobMillimetres / rearTravel:P0} of travel";
        return new(
            $"{UnitsFormatter.FormatDistance(climbing.DistanceMetres)} of climbing in " +
            $"{UnitsFormatter.FormatDuration(TimeSpan.FromSeconds(climbing.Seconds))}, {climbing.AscentMetres:0} m up.",
            $"{climbing.PedalledShare:P0} of climb time",
            bob,
            $"{climbing.CadenceRpm:0} rpm",
            Travel(climbing.Rear, profile.RearMaxTravel),
            Travel(climbing.Front, profile.FrontMaxTravel),
            Ground(climbing));
    }

    private static string? Travel(ClimbTravel travel, double? maxTravel)
    {
        string Value(double millimetres) => maxTravel is > 0 and var max ? $"{millimetres / max:P0}" : $"{millimetres:0} mm";
        var parts = new List<string>();
        if (travel.Climbing is { } climbing) parts.Add($"{Value(climbing)} climbing");
        if (travel.Descending is { } descending) parts.Add($"{Value(descending)} descending");
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }

    private static string? Ground(ClimbingSummary climbing)
    {
        var parts = new List<string>();
        if (climbing.SmoothMetres > 0) parts.Add($"{UnitsFormatter.FormatDistance(climbing.SmoothMetres)} smooth");
        if (climbing.TechnicalMetres > 0) parts.Add($"{UnitsFormatter.FormatDistance(climbing.TechnicalMetres)} technical");
        return parts.Count > 0 ? string.Join(" · ", parts) : null;
    }
}

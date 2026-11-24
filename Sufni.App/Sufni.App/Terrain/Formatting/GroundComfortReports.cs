using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Sufni.App.Shared.Formatting;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Formatting;

internal static class GroundComfortReports
{
    public static GroundComfortReport Build(TerrainProfile profile, TerrainScope scope, TelemetryTimeRange? range,
        SuspensionResponseModel? usual)
    {
        var rows = TerrainComfort.Describe(profile, range, scope.Matches);
        var response = usual is null ? null : TerrainResponse.Describe(profile, usual, range, scope.Matches)
            .Where(row => row.Class is not null).ToDictionary(row => row.Class!.Value);
        var total = rows.Sum(row => row.DistanceMetres);
        var levels = rows.Select(row =>
        {
            var cells = response?.GetValueOrDefault(row.Class)?.Cells;
            string? Usual(ResponseTarget target) => cells?.FirstOrDefault(cell => cell.Target == target) switch
            {
                { State: ResponseCellState.WithinRepeatRange } => "as usual",
                { State: ResponseCellState.Finding, Deviation: { } deviation } => TerrainResponseText.Percent(deviation),
                _ => null
            };
            ComfortFigure Activity(double? value, ResponseTarget target) =>
                value is { } v ? new($"{v:0} mm/s", Usual(target)) : ComfortFigure.Missing;
            return new GroundComfortLevel(row.Class, TerrainClassText.Name(row.Class), UnitsFormatter.FormatDistance(row.DistanceMetres),
                total > 0 ? row.DistanceMetres / total : 0,
                Activity(row.FrontActivity, ResponseTarget.FrontRms), Activity(row.RearActivity, ResponseTarget.RearRms),
                row.FrameLevel is { } frame ? new($"{frame:0.0} g") : ComfortFigure.Missing,
                Usual(ResponseTarget.Transmission));
        }).ToImmutableArray();
        var hasFrame = profile.Windows.Any(window => window.Frame is not null);
        var hasLowers = profile.Windows.Any(window => window.Lowers is not null);
        return new(levels,
            usual is null ? "Comparisons with your usual appear once it is ready." : null,
            !hasFrame ? "Frame vibration needs a frame IMU."
            : !hasLowers ? "Vibration passed to the frame needs a lowers IMU as well." : null);
    }
}

using System;
using System.Collections.Immutable;
using System.Linq;
using Avalonia.Media;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Terrain.Models;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Plots;

internal static class TerrainColors
{
    public static Color ColorFor(this SufniTerrainScales scales, GradeBand band) => scales.Grade[(int)band];

    public static Color ColorFor(this SufniTerrainScales scales, RoughnessClass level) => level.IsClimb()
        ? scales.ClimbRoughness[level - RoughnessClass.SmoothClimb]
        : scales.Roughness[(int)level];

    public static ImmutableArray<TerrainColourRun> Colour<TClass>(this ImmutableArray<TerrainRun<TClass>> runs,
        Func<TClass, Color> colour) where TClass : struct, Enum =>
        [.. runs.Select(run => new TerrainColourRun(run.StartSeconds, run.EndSeconds, colour(run.Class)))];
}

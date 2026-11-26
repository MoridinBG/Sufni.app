using System;
using System.Collections.Generic;
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

    private static readonly double[] RoughnessPositions = [-1.5, -0.5, 0.5, 1.5, 2.5];
    private static readonly double[] GradePositions = [-8, -4, 0, 3.5, 7.5, 12.5, 17.5];

    /// <summary>The colour of a class value: the class colours sit at the class centres and blend linearly between them.</summary>
    public static Color ShadeFor(this SufniTerrainScales scales, RoughnessClass level, double? value) => value is { } v
        ? Blend(scales.Roughness, RoughnessPositions, v)
        : scales.ColorFor(level);

    /// <summary>The colour of a baseline grade in degrees: the band colours sit inside the bands and blend linearly between them.</summary>
    public static Color ShadeFor(this SufniTerrainScales scales, GradeBand band, double? value) => value is { } v
        ? Blend(scales.Grade, GradePositions, v)
        : scales.ColorFor(band);

    private static Color Blend(IReadOnlyList<Color> colours, double[] positions, double value)
    {
        if (value <= positions[0]) return colours[0];
        var upper = 1;
        while (upper < positions.Length - 1 && value > positions[upper]) upper++;
        if (value >= positions[upper]) return colours[upper];
        var t = (value - positions[upper - 1]) / (positions[upper] - positions[upper - 1]);
        var (a, b) = (colours[upper - 1], colours[upper]);
        return Color.FromArgb(Channel(a.A, b.A, t), Channel(a.R, b.R, t), Channel(a.G, b.G, t), Channel(a.B, b.B, t));

        static byte Channel(byte from, byte to, double t) => (byte)Math.Round(from + (to - from) * t);
    }

    public static ImmutableArray<TerrainColourRun> Colour<TClass>(this ImmutableArray<TerrainRun<TClass>> runs,
        Func<TClass, Color> colour) where TClass : struct, Enum =>
        [.. runs.Select(run => new TerrainColourRun(run.StartSeconds, run.EndSeconds, colour(run.Class)))];
}

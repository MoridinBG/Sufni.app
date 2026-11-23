using System;
using System.Collections.Immutable;

namespace Sufni.App.Terrain.Models;

internal sealed class TerrainSignalSections
{
    private readonly ImmutableArray<TerrainSignalSection>.Builder sections = ImmutableArray.CreateBuilder<TerrainSignalSection>();
    private readonly ImmutableArray<double>.Builder times = ImmutableArray.CreateBuilder<double>();
    private readonly ImmutableArray<double>.Builder values = ImmutableArray.CreateBuilder<double>();
    private double? previousEnd;

    public static double? Degrees(double? grade) => grade is { } g ? Math.Asin(Math.Clamp(g, -1, 1)) * 180 / Math.PI : null;

    public void Add(double start, double end, double? value)
    {
        if (value is not { } sample || !double.IsFinite(sample)) return;
        if (previousEnd != start) FinishSection();
        times.Add((start + end) / 2);
        values.Add(sample);
        previousEnd = end;
    }

    public ImmutableArray<TerrainSignalSection> Build()
    {
        FinishSection();
        return sections.ToImmutable();
    }

    private void FinishSection()
    {
        if (times.Count == 0) return;
        sections.Add(new(times.ToImmutable(), values.ToImmutable()));
        times.Clear();
        values.Clear();
    }
}

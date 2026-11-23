using System;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

public sealed record TerrainMaterialization
{
    private TerrainMaterialization(TerrainOutcome outcome, TerrainProfile? profile)
    {
        Outcome = outcome;
        Profile = profile;
        Summary = profile is null ? null : TerrainProfileCalculator.Summarize(profile);
    }

    public TerrainOutcome Outcome { get; }
    public TerrainProfile? Profile { get; }
    public TerrainSummary? Summary { get; }

    public static TerrainMaterialization Computed(TerrainProfile profile) =>
        new(TerrainOutcome.Computed, profile ?? throw new ArgumentNullException(nameof(profile)));

    public static TerrainMaterialization WithoutProfile(TerrainOutcome outcome) => outcome switch
    {
        TerrainOutcome.Computed => throw new ArgumentException("A computed result requires a profile.", nameof(outcome)),
        _ => new(outcome, null)
    };
}

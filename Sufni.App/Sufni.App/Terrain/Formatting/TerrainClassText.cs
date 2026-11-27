using System;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Formatting;

internal static class TerrainClassText
{
    public static string Name(GradeBand band) => band switch
    {
        GradeBand.SteepClimb => "Steep climb",
        GradeBand.Climb => "Climb",
        GradeBand.Flat => "Flat",
        GradeBand.GentleDescent => "Gentle descent",
        GradeBand.Descent => "Descent",
        GradeBand.SteepDescent => "Steep descent",
        GradeBand.VerySteepDescent => "Very steep descent",
        _ => throw new ArgumentOutOfRangeException(nameof(band))
    };

    public static string Name(RoughnessClass level) => level switch
    {
        RoughnessClass.Smooth => "Smooth",
        RoughnessClass.Calm => "Calm",
        RoughnessClass.Medium => "Medium",
        RoughnessClass.Rough => "Rough",
        RoughnessClass.VeryRough => "Very rough",
        RoughnessClass.SmoothClimb => "Smooth climb",
        RoughnessClass.TechnicalClimb => "Technical climb",
        _ => throw new ArgumentOutOfRangeException(nameof(level))
    };

    /// <summary>The class a labelled stretch reads, only where it differs from the label.</summary>
    public static string? ReadsAs(RoughnessClass label, LabelReading? reading) =>
        reading is { } read && read.Class != label ? $"Reads as {Name(read.Class)}" : null;
}

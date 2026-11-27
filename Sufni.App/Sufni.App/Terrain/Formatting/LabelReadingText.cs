using System.Globalization;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Formatting;

/// <summary>What the stretch of a label reads, only where it differs from the label; whether it clashes; and the tooltip.</summary>
public sealed record LabelReadingText(string? Text, bool Clashes, string ToolTip)
{
    public static LabelReadingText Of(RoughnessClass label, LabelReading? reading) => new(
        reading is { } read && read.Class != label ? $"Reads as {TerrainClassText.Name(read.Class)}" : null,
        reading?.Clashes == true,
        "How the labelled stretch reads on the scale set without this label. Yellow: more than one class away from its label.");

    public static LabelReadingText Of(GradeBand label, GradeReading? reading) => new(
        reading is { } read && read.Band != label
            ? string.Create(CultureInfo.InvariantCulture, $"Reads as {TerrainClassText.Name(read.Band)} ({read.Degrees:0}°)")
            : null,
        reading?.Clashes == true,
        "The band of the mean measured grade of the labelled stretch. Yellow: more than 2° outside the band of its label.");
}

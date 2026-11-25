using System.Collections.Immutable;
using System.Linq;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Models;

/// <summary>A measured figure with its deviation from your usual: "as usual", a signed percent, or null without a usual.</summary>
public sealed record ComfortFigure(string Measured, string? Usual = null)
{
    public static ComfortFigure Missing { get; } = new("—");
    public bool HasUsual => Usual is not null;

    /// <summary>The deviation where your usual knows this ground; the measured figure otherwise.</summary>
    public string Summary => Usual ?? Measured;
}

/// <summary>
/// One roughness level in scope: the ground, what the suspension absorbed, and what reached the frame. Vibration passed
/// to the frame exists only as a deviation from your usual.
/// </summary>
public sealed record GroundComfortLevel(RoughnessClass Class, string Name, string Distance, double Share,
    ComfortFigure Fork, ComfortFigure Shock, ComfortFigure Frame, string? PassedToFrame)
{
    public bool HasPassedToFrame => PassedToFrame is not null;
}

/// <summary>
/// How the ground in scope reached the rider, level by level. Notes name what a missing sensor or baseline leaves out.
/// </summary>
public sealed record GroundComfortReport(ImmutableArray<GroundComfortLevel> Levels, string? UsualNote, string? FrameNote)
{
    public bool HasUsualNote => UsualNote is not null;
    public bool HasFrameNote => FrameNote is not null;
    public bool HasPassedToFrame => Levels.Any(level => level.HasPassedToFrame);
}

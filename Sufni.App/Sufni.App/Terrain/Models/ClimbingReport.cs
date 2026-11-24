namespace Sufni.App.Terrain.Models;

/// <summary>
/// What the climbs in the time range showed: how long they were, how much the rear bobbed while pedalling, and where
/// each end sat in its travel against the descents. Figures only, no advice.
/// </summary>
public sealed record ClimbingReport(string Summary, string Pedalling, string Bob, string Cadence, string? ShockTravel,
    string? ForkTravel, string? Ground)
{
    public bool HasShockTravel => ShockTravel is not null;
    public bool HasForkTravel => ForkTravel is not null;
    public bool HasGround => Ground is not null;
}

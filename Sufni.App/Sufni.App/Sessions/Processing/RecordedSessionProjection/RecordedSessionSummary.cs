using Sufni.App.Video.Stores;
using System;

using Sufni.App.Terrain.Models;
using Sufni.Telemetry;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Lightweight recorded-session row state.
/// It carries the fields needed to present and sort a session list together
/// with the current processed-data staleness classification and update
/// baseline for list-level recomputation.
/// </summary>
public sealed record RecordedSessionSummary(
    Guid Id,
    long Updated,
    string Name,
    string Description,
    long? Timestamp,
    bool HasProcessedData,
    SessionStaleness Staleness,
    double? DurationSeconds = null,
    double? DistanceMeters = null,
    double? AscentMeters = null,
    double? DescentMeters = null,
    SessionVideoSnapshot? LatestVideo = null,
    Guid? BikeId = null,
    Guid? SetupId = null)
{
    public required TerrainStatus Terrain { get; init; }
    public TerrainScaleKey? TerrainScale { get; init; }
    public TerrainClassState TerrainClasses { get; init; } = new TerrainClassState.Unavailable();
}

namespace Sufni.App.Video.Models;

/// <summary>
/// The source identity, window origin and duration used to select and play clips.
/// </summary>
public sealed record VideoSessionTiming(string? OwnerKey, double WindowStartSeconds, double? DurationSeconds);

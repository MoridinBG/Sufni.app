using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.Models;

namespace Sufni.App.MapsAndTracks.Models;

/// <summary>
/// A compared segment match on the map: the matched session's track and the
/// matched segment of it.
/// </summary>
public sealed record MatchedTrackOverlay(
    IReadOnlyList<TrackPoint> SessionTrack,
    IReadOnlyList<TrackPoint> SegmentTrack);

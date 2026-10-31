using System;

namespace Sufni.App.MapsAndTracks.Models;

public sealed record GpsMetricsKey(
    Guid? TrackContentId,
    long? Timestamp,
    double? DurationSeconds,
    double GpsOffsetSeconds,
    double WindowOriginFraction,
    int ProjectionVersion,
    int MetricsAlgorithmVersion);

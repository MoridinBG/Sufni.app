using System;

namespace Sufni.App.MapsAndTracks.Stores;

public sealed record TrackSnapshot(Guid Id, double StartTime, double EndTime, long PointsRevision);

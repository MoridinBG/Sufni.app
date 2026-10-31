using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sufni.App.Infrastructure;
using Sufni.App.Shared.Common;

namespace Sufni.App.MapsAndTracks.Models;

/// <summary>Stable identity of ordered track points, including timing and GPS quality.</summary>
public static class TrackContentHash
{
    // Permanent namespace and byte-stable point JSON define the cross-device identity.
    private static readonly Guid TrackNamespace = new("3aa8c1dc-e3b2-4cd9-80b8-96a2f27ca3b0");

    public static Guid Compute(IReadOnlyList<TrackPoint> points) =>
        UuidUtil.CreateVersion5(TrackNamespace, Encoding.UTF8.GetBytes(AppJson.Serialize(points.ToList())));
}

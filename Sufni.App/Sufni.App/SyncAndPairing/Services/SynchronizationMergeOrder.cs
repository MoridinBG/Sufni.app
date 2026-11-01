using System;
using Sufni.App.SyncAndPairing.Models;

namespace Sufni.App.SyncAndPairing.Services;

internal static class SynchronizationMergeOrder
{
    public static int Compare(Synchronizable incoming, Synchronizable current)
    {
        var version = incoming.Version.CompareTo(current.Version);
        if (version != 0) return version;
        var deletion = incoming.Deleted.HasValue.CompareTo(current.Deleted.HasValue);
        if (deletion != 0) return deletion;
        var authoredTime = incoming.Updated.CompareTo(current.Updated);
        return authoredTime != 0 ? authoredTime : StringComparer.Ordinal.Compare(incoming.UpdatedBy, current.UpdatedBy);
    }
}

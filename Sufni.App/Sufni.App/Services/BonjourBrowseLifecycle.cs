using System;
using System.Collections.Generic;

namespace Sufni.App.Services;

internal sealed class BonjourBrowseLifecycle<TPending>
{
    private readonly Dictionary<string, PendingResolution> pendingByKey = [];
    private readonly Dictionary<string, ServiceAnnouncement> resolvedByKey = [];
    private long nextResolutionId;
    private bool isBrowsing;

    public bool TryStart()
    {
        if (isBrowsing)
        {
            return false;
        }

        isBrowsing = true;
        return true;
    }

    public bool TryStop(Action<TPending> cancelPending)
    {
        if (!isBrowsing)
        {
            return false;
        }

        isBrowsing = false;
        foreach (var pending in pendingByKey.Values)
        {
            cancelPending(pending.Value);
        }

        pendingByKey.Clear();
        resolvedByKey.Clear();
        return true;
    }

    public long TrackPending(string key, TPending pending, Action<TPending> cancelPending)
    {
        if (pendingByKey.Remove(key, out var existingPending))
        {
            cancelPending(existingPending.Value);
        }

        var resolutionId = ++nextResolutionId;
        pendingByKey[key] = new PendingResolution(resolutionId, pending);
        return resolutionId;
    }

    public bool CancelPending(string key, Action<TPending> cancelPending)
    {
        if (!pendingByKey.Remove(key, out var pending))
        {
            return false;
        }

        cancelPending(pending.Value);
        return true;
    }

    public bool TryResolve(string key, long resolutionId, ServiceAnnouncement announcement)
    {
        if (!pendingByKey.TryGetValue(key, out var pending) || pending.ResolutionId != resolutionId)
        {
            return false;
        }

        pendingByKey.Remove(key);
        resolvedByKey[key] = announcement;
        return true;
    }

    public bool TryRemoveResolved(string key, out ServiceAnnouncement? announcement)
    {
        if (!resolvedByKey.Remove(key, out var removed))
        {
            announcement = null;
            return false;
        }

        announcement = removed;
        return true;
    }

    private sealed record PendingResolution(long ResolutionId, TPending Value);
}
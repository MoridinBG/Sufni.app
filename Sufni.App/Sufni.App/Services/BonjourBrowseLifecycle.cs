using System;
using System.Collections.Generic;

namespace Sufni.App.Services;

internal sealed class BonjourBrowseLifecycle<TPending>
{
    private readonly object gate = new();
    private readonly Dictionary<string, PendingResolution> pendingByKey = [];
    private readonly Dictionary<string, ServiceAnnouncement> resolvedByKey = [];
    private long nextResolutionId;
    private bool isBrowsing;

    public bool TryStart()
    {
        lock (gate)
        {
            if (isBrowsing)
            {
                return false;
            }

            isBrowsing = true;
            return true;
        }
    }

    public bool TryStop(Action<TPending> cancelPending)
    {
        List<TPending> pendingToCancel;
        lock (gate)
        {
            if (!isBrowsing)
            {
                return false;
            }

            isBrowsing = false;
            pendingToCancel = new List<TPending>(pendingByKey.Count);
            foreach (var pending in pendingByKey.Values)
            {
                pendingToCancel.Add(pending.Value);
            }

            pendingByKey.Clear();
            resolvedByKey.Clear();
        }

        foreach (var pending in pendingToCancel)
        {
            cancelPending(pending);
        }

        return true;
    }

    public bool TryTrackPending(string key, TPending pending, Action<TPending> cancelPending, out long resolutionId)
    {
        PendingResolution? existingPending = null;
        var shouldCancelPending = false;

        lock (gate)
        {
            if (!isBrowsing)
            {
                resolutionId = 0;
                shouldCancelPending = true;
            }
            else
            {
                if (pendingByKey.Remove(key, out var removedPending))
                {
                    existingPending = removedPending;
                }

                resolutionId = ++nextResolutionId;
                pendingByKey[key] = new PendingResolution(resolutionId, pending);
            }
        }

        if (existingPending is not null)
        {
            cancelPending(existingPending.Value);
        }

        if (shouldCancelPending)
        {
            cancelPending(pending);
            return false;
        }

        return true;
    }

    public bool CancelPending(string key, Action<TPending> cancelPending)
    {
        PendingResolution? pending = null;
        lock (gate)
        {
            if (!pendingByKey.Remove(key, out var removedPending))
            {
                return false;
            }

            pending = removedPending;
        }

        cancelPending(pending.Value);
        return true;
    }

    public bool TryResolve(string key, long resolutionId, ServiceAnnouncement announcement)
    {
        lock (gate)
        {
            if (!pendingByKey.TryGetValue(key, out var pending) || pending.ResolutionId != resolutionId)
            {
                return false;
            }

            pendingByKey.Remove(key);
            resolvedByKey[key] = announcement;
            return true;
        }
    }

    public bool TryRemoveResolved(string key, out ServiceAnnouncement? announcement)
    {
        lock (gate)
        {
            if (!resolvedByKey.Remove(key, out var removed))
            {
                announcement = null;
                return false;
            }

            announcement = removed;
            return true;
        }
    }

    private sealed record PendingResolution(long ResolutionId, TPending Value);
}
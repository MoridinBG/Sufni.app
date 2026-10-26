using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Stores;
using Sufni.App.Sessions.Store;
namespace Sufni.App.Segments.Coordinators;

public abstract record SegmentChangeResult
{
    private SegmentChangeResult() { }

    public sealed record Applied : SegmentChangeResult;
    public sealed record Failed(string ErrorMessage) : SegmentChangeResult;
}

/// <summary>
/// Owns the saved-segment workflows: create, rename, and delete. Segment
/// names are unique among the live segments of one session.
/// </summary>
public interface ISegmentCoordinator
{
    Task<SegmentChangeResult> CreateAsync(Guid sessionId, string name, double startSeconds, double endSeconds);

    Task<SegmentChangeResult> RenameAsync(Guid segmentId, string name);

    Task<SegmentChangeResult> DeleteAsync(Guid segmentId);

    /// <summary>
    /// Returns the error for a segment name in the session, or null when the
    /// name is valid. <paramref name="segmentId"/> is the renamed segment.
    /// </summary>
    string? ValidateName(Guid sessionId, string name, Guid? segmentId = null);

    /// <summary>
    /// Returns the first free "Segment N" name in the session.
    /// </summary>
    string NextName(Guid sessionId);
}

internal sealed class SegmentCoordinator(
    ISessionSegmentRepository repository,
    ISessionSegmentStoreWriter segmentStore,
    ISessionStore sessionStore) : ISegmentCoordinator
{
    private static readonly ILogger logger = Log.ForContext<SegmentCoordinator>();
    private static readonly SegmentChangeResult MissingSegment =
        new SegmentChangeResult.Failed("Saved segment no longer exists.");

    public async Task<SegmentChangeResult> CreateAsync(Guid sessionId, string name, double startSeconds, double endSeconds)
    {
        if (sessionStore.Get(sessionId) is null)
        {
            return new SegmentChangeResult.Failed("Session no longer exists.");
        }

        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || startSeconds >= endSeconds)
        {
            return new SegmentChangeResult.Failed("Saved segment range is invalid.");
        }

        var trimmedName = name.Trim();
        if (ValidateName(sessionId, trimmedName) is { } error)
        {
            return new SegmentChangeResult.Failed(error);
        }

        var segment = new SessionSegment
        {
            SessionId = sessionId,
            Name = trimmedName,
            StartSeconds = startSeconds,
            EndSeconds = endSeconds,
        };
        return await WriteAsync("saved", async () =>
        {
            await repository.InsertAsync(segment);
            return segment;
        });
    }

    public async Task<SegmentChangeResult> RenameAsync(Guid segmentId, string name)
    {
        var trimmedName = name.Trim();
        if (segmentStore.Get(segmentId) is not { } existing)
        {
            return MissingSegment;
        }

        if (ValidateName(existing.SessionId, trimmedName, segmentId) is { } error)
        {
            return new SegmentChangeResult.Failed(error);
        }

        return await WriteAsync("renamed", () => repository.RenameAsync(segmentId, trimmedName));
    }

    public Task<SegmentChangeResult> DeleteAsync(Guid segmentId) =>
        WriteAsync("deleted", () => repository.DeleteAsync(segmentId));

    public string? ValidateName(Guid sessionId, string name, Guid? segmentId = null)
    {
        var trimmedName = name.Trim();
        if (trimmedName.Length == 0)
        {
            return "Segment name is required.";
        }

        return segmentStore.GetForSession(sessionId).Any(segment =>
            segment.Id != segmentId && StringComparer.Ordinal.Equals(segment.Name, trimmedName))
            ? "A saved segment with this name already exists."
            : null;
    }

    public string NextName(Guid sessionId)
    {
        var names = segmentStore.GetForSession(sessionId).Select(segment => segment.Name).ToHashSet(StringComparer.Ordinal);
        var index = 1;
        while (names.Contains($"Segment {index}"))
        {
            index++;
        }

        return $"Segment {index}";
    }

    private async Task<SegmentChangeResult> WriteAsync(string action, Func<Task<SessionSegment?>> write)
    {
        try
        {
            if (await write() is not { } segment)
            {
                return MissingSegment;
            }

            await segmentStore.PublishSegmentsAsync([segment]);
            return new SegmentChangeResult.Applied();
        }
        catch (Exception e)
        {
            logger.Error(e, "Saved segment could not be {Action}", action);
            return new SegmentChangeResult.Failed($"Saved segment could not be {action}: {e.Message}");
        }
    }
}

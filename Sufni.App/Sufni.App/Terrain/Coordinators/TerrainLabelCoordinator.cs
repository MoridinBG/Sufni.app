using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Shared.Models;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Services;
using Sufni.App.Terrain.Stores;
using Sufni.Telemetry;

namespace Sufni.App.Terrain.Coordinators;

public closed record TerrainLabelChangeResult
{
    private TerrainLabelChangeResult() { }

    public sealed record Applied : TerrainLabelChangeResult;
    public sealed record SessionMissing : TerrainLabelChangeResult;
    public sealed record SourceUnavailable : TerrainLabelChangeResult;
    public sealed record LabelMissing : TerrainLabelChangeResult;
    public sealed record Invalid(string Message) : TerrainLabelChangeResult;
    public sealed record Failed(TerrainLabelWriteFailureReason Reason, string? Details = null) : TerrainLabelChangeResult;
}

/// <summary>
/// Owns the terrain label workflows: create, change the class and delete. The labels of one recording source never
/// overlap when this device creates them.
/// </summary>
public interface ITerrainLabelCoordinator
{
    /// <param name="startSeconds">Telemetry seconds of the session.</param>
    Task<TerrainLabelChangeResult> CreateAsync(Guid sessionId, RoughnessClass roughnessClass, double startSeconds, double endSeconds);

    Task<TerrainLabelChangeResult> SetClassAsync(Guid labelId, RoughnessClass roughnessClass);

    Task<TerrainLabelChangeResult> DeleteAsync(Guid labelId);

    /// <summary>
    /// Returns the error for a new label over the range of the session, or null when the range is free.
    /// </summary>
    string? ValidateRange(Guid sessionId, double startSeconds, double endSeconds);
}

internal sealed class TerrainLabelCoordinator(
    ITerrainLabelRepository repository,
    ITerrainLabelStoreWriter labelStore,
    ISessionStore sessionStore,
    IRecordedSessionSourceStore sourceStore,
    IBackgroundTaskRunner backgroundTaskRunner) : ITerrainLabelCoordinator
{
    private static readonly ILogger logger = Log.ForContext<TerrainLabelCoordinator>();

    public async Task<TerrainLabelChangeResult> CreateAsync(Guid sessionId, RoughnessClass roughnessClass, double startSeconds,
        double endSeconds)
    {
        if (sessionStore.Get(sessionId) is not { } session) return new TerrainLabelChangeResult.SessionMissing();
        if (sourceStore.Get(session.RecordingSourceSessionId) is not { } source) return new TerrainLabelChangeResult.SourceUnavailable();
        if (!double.IsFinite(startSeconds) || !double.IsFinite(endSeconds) || startSeconds >= endSeconds)
        {
            logger.Warning("Invalid terrain label range for session {SessionId}", sessionId);
            return new TerrainLabelChangeResult.Invalid("The label range is invalid.");
        }

        var timeline = new SessionTrackTimeline(0, OwnerOriginSeconds: session.WindowStartSeconds);
        var label = new TerrainLabel
        {
            OwnerKey = source.SourceHash,
            StartSeconds = timeline.TelemetryToOwnerSeconds(startSeconds),
            EndSeconds = timeline.TelemetryToOwnerSeconds(endSeconds),
            Class = roughnessClass
        };
        if (OverlapError(label.OwnerKey, label.StartSeconds, label.EndSeconds) is { } error)
        {
            logger.Warning("Overlapping terrain label for session {SessionId}", sessionId);
            return new TerrainLabelChangeResult.Invalid(error);
        }

        return await WriteAsync(TerrainLabelWriteFailureReason.Create, label.Id, async () =>
        {
            await repository.InsertAsync(label);
            return label;
        });
    }

    public Task<TerrainLabelChangeResult> SetClassAsync(Guid labelId, RoughnessClass roughnessClass) =>
        WriteAsync(TerrainLabelWriteFailureReason.SetClass, labelId, () => repository.SetClassAsync(labelId, roughnessClass));

    public Task<TerrainLabelChangeResult> DeleteAsync(Guid labelId) =>
        WriteAsync(TerrainLabelWriteFailureReason.Delete, labelId, () => repository.DeleteAsync(labelId));

    public string? ValidateRange(Guid sessionId, double startSeconds, double endSeconds)
    {
        if (sessionStore.Get(sessionId) is not { } session ||
            sourceStore.Get(session.RecordingSourceSessionId) is not { } source) return null;
        var timeline = new SessionTrackTimeline(0, OwnerOriginSeconds: session.WindowStartSeconds);
        return OverlapError(source.SourceHash, timeline.TelemetryToOwnerSeconds(startSeconds),
            timeline.TelemetryToOwnerSeconds(endSeconds));
    }

    private string? OverlapError(string ownerKey, double startSeconds, double endSeconds) =>
        labelStore.GetForOwner(ownerKey).Any(label => label.StartSeconds < endSeconds && startSeconds < label.EndSeconds)
            ? "The range overlaps a terrain label."
            : null;

    private async Task<TerrainLabelChangeResult> WriteAsync(TerrainLabelWriteFailureReason reason, Guid labelId,
        Func<Task<TerrainLabel?>> write)
    {
        try
        {
            if (await backgroundTaskRunner.RunAsync(write) is not { } label) return new TerrainLabelChangeResult.LabelMissing();
            await labelStore.PublishLabelsAsync(
                label.Deleted is null ? [TerrainLabelSnapshot.From(label)] : [],
                label.Deleted is not null ? [label.Id] : []);
            return new TerrainLabelChangeResult.Applied();
        }
        catch (Exception e)
        {
            logger.Error(e, "Terrain label write failed for {LabelId} during {Operation}", labelId, reason);
            return new TerrainLabelChangeResult.Failed(reason, e.Message);
        }
    }
}

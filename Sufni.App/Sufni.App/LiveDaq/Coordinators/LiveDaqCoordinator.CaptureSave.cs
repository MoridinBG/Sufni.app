using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Services;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Models;

namespace Sufni.App.LiveDaq.Coordinators;

public partial class LiveDaqCoordinator
{
    public async Task<LiveSessionSaveResult> SaveCaptureAsync(
        SessionSnapshot draft,
        ILiveSessionService captureService,
        SessionPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        logger.Information("Starting live session save for {SessionId}", draft.Id);

        try
        {
            return await backgroundTaskRunner.RunAsync<LiveSessionSaveResult>(async () =>
            {
                var capture = await captureService.PrepareCaptureForSaveAsync(cancellationToken);
                var source = RecordedSessionSourceFactory.CreateLiveCapture(capture.TelemetryCapture);
                var setup = await setupRepository.GetAsync(capture.Context.SetupId)
                            ?? throw new InvalidOperationException("Setup is missing.");
                var bike = await bikeRepository.GetAsync(setup.BikeId)
                           ?? throw new InvalidOperationException("Bike is missing.");
                var setupSnapshot = SetupSnapshot.From(setup);
                var bikeSnapshot = BikeSnapshot.From(bike);
                var sourceSnapshot = RecordedSessionSourceSnapshot.From(source);
                var sessionSnapshot = draft with { Id = source.SessionId, Timestamp = capture.TelemetryCapture.Metadata.Timestamp };
                var input = new RecordedSessionDerivationInput(
                    sessionSnapshot, setupSnapshot, bikeSnapshot, sourceSnapshot, source.Payload);

                var reprocessResult = await recordedSessionReprocessor.ReprocessAsync(input, cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                if (reprocessResult is RecordedSessionReprocessResult.Failed failure)
                    return new LiveSessionSaveResult.ProcessingFailed(failure.Reason, failure.Details);
                var processed = (RecordedSessionReprocessResult.Processed)reprocessResult;
                var generation = processed.Generation;
                var newTrack = generation.TrackPoints is { Count: > 0 } points
                    ? new Track(points) : null;
                var prepared = generation.CreateSession(sessionSnapshot);
                var committed = await sessionRepository.ImportProcessedSessionAsync(prepared, newTrack, source);
                var fresh = committed.Session
                    ?? throw new InvalidOperationException("The capture source exists, but its recorded session is no longer available.");

                var snapshot = SessionSnapshot.From(fresh);
                string? preferenceWarning = null;
                try
                {
                    await sessionPreferences.UpdateRecordedAsync(snapshot.Id, _ => preferences);
                }
                catch (Exception e)
                {
                    preferenceWarning = e.Message;
                    logger.Warning(e, "Failed to update recorded-session preferences after saving session {SessionId}", draft.Id);
                }

                string? publicationWarning = null;
                try
                {
                    var tracks = await trackQueries.GetTrackSnapshotsAsync(newTrack is { } track ? [track.Id] : []);
                    await dispatcher.InvokeAsync(async () =>
                    {
                        using var batch = publication.BeginBatch();
                        await sessionStore.PublishSessionsChangedAsync([snapshot]);
                        await sourceStore.PublishSourcesChangedAsync([committed.Source]);
                        await trackStore.PublishTracksChangedAsync(tracks);
                    });
                }
                catch (Exception e)
                {
                    publicationWarning = e.Message;
                    logger.Warning(e, "Failed to publish live session state after saving session {SessionId}", draft.Id);
                }

                string? cleanupFailure = null;
                try { await captureService.ResetCaptureAsync(); }
                catch (Exception e)
                {
                    cleanupFailure = e.Message;
                    logger.Warning(e, "Failed to reset live capture after saving session {SessionId}", snapshot.Id);
                }

                logger.Information("Live session save completed for {SessionId}", draft.Id);
                return new LiveSessionSaveResult.Saved(
                    snapshot.Id,
                    capture.CaptureRevision,
                    publicationWarning,
                    cleanupFailure,
                    preferenceWarning);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "Live session save failed for {SessionId}", draft.Id);
            return new LiveSessionSaveResult.Failed(LiveSessionSaveFailureReason.Save, e.Message);
        }
    }

}

public abstract record LiveSessionSaveResult
{
    private LiveSessionSaveResult() { }

    public sealed record Saved(
        Guid SessionId,
        long CaptureRevision,
        string? PublicationWarning = null,
        string? CleanupFailure = null,
        string? PreferenceWarning = null) : LiveSessionSaveResult;
    public sealed record Failed(LiveSessionSaveFailureReason Reason, string? Details = null) : LiveSessionSaveResult;
    public sealed record ProcessingFailed(SessionProcessingFailureReason Reason, string? Details = null) : LiveSessionSaveResult;
}


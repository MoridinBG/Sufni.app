using Sufni.App.MapsAndTracks.Stores;
using Sufni.App.MapsAndTracks.Models;
using System;
using Sufni.Telemetry;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Builds a one-shot domain snapshot for a recorded session.
/// The snapshot captures the session metadata, setup and bike dependency
/// state, raw-source metadata, fingerprint state, and staleness together.
/// </summary>
public sealed class RecordedSessionDomainQuery(
    ISessionStore sessionStore,
    ISetupStore setupStore,
    IBikeStore bikeStore,
    IRecordedSessionSourceStore sourceStore,
    IProcessingFingerprintService fingerprintService,
    IProcessingDependencyHashIndex dependencyHashIndex,
    ITrackStore trackStore) : IRecordedSessionDomainQuery
{
    public RecordedSessionDomainSnapshot? Get(Guid sessionId)
    {
        var session = sessionStore.Get(sessionId);
        var setup = session?.SetupId is { } setupId
            ? setupStore.Get(setupId)
            : null;
        return session is null
            ? null
            : RecordedSessionDomainSnapshotFactory.Create(
                session,
                setup,
                setup is null ? null : bikeStore.Get(setup.BikeId),
                sourceStore.Get(session.RecordingSourceSessionId),
                fingerprintService,
                setup is null ? null : dependencyHashIndex.GetForSetup(setup.Id),
                SessionTrackSelection.Select(trackStore.GetAll(), session.Timestamp, session.DurationSeconds, session.GpsOffsetSeconds, session.WindowStartSeconds));
    }
}

internal static class RecordedSessionDomainSnapshotFactory
{
    public static RecordedSessionDomainSnapshot Create(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        IProcessingFingerprintService fingerprintService,
        string? dependencyHash,
        TrackSnapshot? selectedTrack = null)
    {
        var effectiveDependencyHash = setup is null || bike is null
            ? null
            : dependencyHash ?? ProcessingDependencyHash.Compute(setup, bike);
        var evaluation = fingerprintService.EvaluateState(session, setup, bike, source, effectiveDependencyHash);

        return new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            evaluation.Current,
            evaluation.Persisted,
            source,
            evaluation.Staleness,
            effectiveDependencyHash,
            selectedTrack);
    }
}

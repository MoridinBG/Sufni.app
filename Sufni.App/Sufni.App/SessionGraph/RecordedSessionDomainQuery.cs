using System;
using Sufni.App.Stores;

namespace Sufni.App.SessionGraph;

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
    IProcessingFingerprintService fingerprintService) : IRecordedSessionDomainQuery
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
                bikeStore,
                sourceStore.Get(session.Id),
                fingerprintService,
                DerivedChangeKind.None);
    }
}

internal static class RecordedSessionDomainSnapshotFactory
{
    public static RecordedSessionDomainSnapshot Create(
        SessionSnapshot session,
        SetupSnapshot? setup,
        IBikeStore bikeStore,
        RecordedSessionSourceSnapshot? source,
        IProcessingFingerprintService fingerprintService,
        DerivedChangeKind changeKind)
    {
        var bike = setup is null ? null : bikeStore.Get(setup.BikeId);
        return Create(session, setup, bike, source, fingerprintService, changeKind);
    }

    public static RecordedSessionDomainSnapshot Create(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        IProcessingFingerprintService fingerprintService,
        DerivedChangeKind changeKind)
    {
        var evaluation = fingerprintService.EvaluateState(session, setup, bike, source);

        return new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            evaluation.Current,
            evaluation.Persisted,
            source,
            evaluation.Staleness,
            changeKind);
    }
}

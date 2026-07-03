using System;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
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
    IRecordedSessionProcessingOptionCache processingOptionCache,
    IRecordedSessionDerivationWindowCache derivationWindowCache) : IRecordedSessionDomainQuery
{
    public RecordedSessionDomainSnapshot? Get(Guid sessionId)
    {
        var session = sessionStore.Get(sessionId);
        var setup = session?.SetupId is { } setupId
            ? setupStore.Get(setupId)
            : null;
        var window = session is null
            ? null
            : derivationWindowCache.Get(session.Id);
        return session is null
            ? null
            : RecordedSessionDomainSnapshotFactory.Create(
                session,
                setup,
                bikeStore,
                sourceStore.Get(window?.SourceSessionId ?? session.Id),
                fingerprintService,
                processingOptionCache.Get(session.Id),
                window,
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
        TelemetryProcessingOptions options,
        RecordedSessionDerivationWindow? window,
        DerivedChangeKind changeKind)
    {
        var bike = setup is null ? null : bikeStore.Get(setup.BikeId);
        return Create(session, setup, bike, source, fingerprintService, options, window, changeKind);
    }

    public static RecordedSessionDomainSnapshot Create(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        IProcessingFingerprintService fingerprintService,
        TelemetryProcessingOptions options,
        RecordedSessionDerivationWindow? window,
        DerivedChangeKind changeKind)
    {
        var evaluation = fingerprintService.EvaluateState(session, setup, bike, source, options, window);

        return new RecordedSessionDomainSnapshot(
            session,
            setup,
            bike,
            evaluation.Current,
            evaluation.Persisted,
            source,
            window,
            evaluation.Staleness,
            changeKind);
    }
}

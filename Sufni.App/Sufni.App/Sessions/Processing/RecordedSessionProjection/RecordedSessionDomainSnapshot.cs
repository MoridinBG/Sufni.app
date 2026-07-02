using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Complete derived state for one recorded session.
/// It combines the persisted session snapshot, processing dependencies,
/// recorded-source metadata, fingerprint comparison values, staleness, and the
/// reason for the latest emitted change.
/// </summary>
public sealed record RecordedSessionDomainSnapshot(
    SessionSnapshot Session,
    SetupSnapshot? Setup,
    BikeSnapshot? Bike,
    ProcessingFingerprint? CurrentFingerprint,
    ProcessingFingerprint? PersistedFingerprint,
    RecordedSessionSourceSnapshot? Source,
    SessionStaleness Staleness,
    DerivedChangeKind ChangeKind);

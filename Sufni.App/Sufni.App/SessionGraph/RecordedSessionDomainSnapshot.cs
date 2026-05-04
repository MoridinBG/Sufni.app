using Sufni.App.Stores;

namespace Sufni.App.SessionGraph;

public sealed record RecordedSessionDomainSnapshot(
    SessionSnapshot Session,
    SetupSnapshot? Setup,
    BikeSnapshot? Bike,
    ProcessingFingerprint? CurrentFingerprint,
    ProcessingFingerprint? PersistedFingerprint,
    RecordedSessionSourceSnapshot? Source,
    SessionStaleness Staleness,
    DerivedChangeKind ChangeKind);

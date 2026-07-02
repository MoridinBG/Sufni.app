using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;
using Sufni.Telemetry;

using Sufni.App.Bikes.Stores;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
namespace Sufni.App.Sessions.Processing.RecordedSessionProjection;

/// <summary>
/// Defines fingerprint creation, parsing, and staleness evaluation for
/// processed recorded-session data. The service treats fingerprints as the
/// compact description of the inputs that produced a processed cache.
/// </summary>
public interface IProcessingFingerprintService
{
    ProcessingFingerprint CreateCurrent(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSourceSnapshot source,
        TelemetryProcessingOptions? options = null);

    ProcessingFingerprint CreateCurrent(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSourceSnapshot source,
        string dependencyHash,
        TelemetryProcessingOptions? options = null);

    /// <summary>
    /// Builds a fingerprint from the DB-resident processing inputs only (setup,
    /// bike, source, versions), without the preference-stored processing option.
    /// The in-transaction recompute coherence guard uses this together with
    /// <see cref="ProcessingFingerprint.MatchesDatabaseInputs"/> because it cannot
    /// read the option inside a SQLite transaction.
    /// </summary>
    ProcessingFingerprint CreateCurrentDatabaseInputs(
        SessionSnapshot session,
        SetupSnapshot setup,
        BikeSnapshot bike,
        RecordedSessionSourceSnapshot source);

    ProcessingFingerprint CreateCurrentDatabaseInputs(SessionProcessingInputBundle input);

    ProcessingFingerprint? ParsePersisted(SessionSnapshot session);

    /// <summary>
    /// The schema version stamped into fingerprints created by this build. The
    /// sync merge uses it to recognize current-schema fingerprints (a pre-current
    /// or unparseable fingerprint is "legacy" and defers BLOB swaps to the one-time normalization pass).
    /// </summary>
    int CurrentSchemaVersion { get; }

    /// <summary>
    /// Parses a persisted fingerprint JSON string, returning null when it is
    /// absent or cannot be deserialized. Used by the sync merge to compare the
    /// schema versions of the local and remote fingerprints.
    /// </summary>
    ProcessingFingerprint? Parse(string? fingerprintJson);

    SessionStaleness Evaluate(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        TelemetryProcessingOptions? options = null);

    ProcessingFingerprintEvaluation EvaluateState(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source,
        TelemetryProcessingOptions? options = null);
}

/// <summary>
/// Combined fingerprint evaluation result for a recorded-session state.
/// It carries the current input fingerprint when one can be computed, the
/// persisted fingerprint when one can be parsed, and the resulting staleness.
/// </summary>
public sealed record ProcessingFingerprintEvaluation(
    ProcessingFingerprint? Current,
    ProcessingFingerprint? Persisted,
    SessionStaleness Staleness);

using Sufni.App.Stores;

namespace Sufni.App.SessionGraph;

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
        RecordedSessionSourceSnapshot source);

    ProcessingFingerprint? ParsePersisted(SessionSnapshot session);

    SessionStaleness Evaluate(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source);

    ProcessingFingerprintEvaluation EvaluateState(
        SessionSnapshot session,
        SetupSnapshot? setup,
        BikeSnapshot? bike,
        RecordedSessionSourceSnapshot? source);
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

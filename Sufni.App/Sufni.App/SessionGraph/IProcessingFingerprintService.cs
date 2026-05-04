using Sufni.App.Stores;

namespace Sufni.App.SessionGraph;

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

public sealed record ProcessingFingerprintEvaluation(
    ProcessingFingerprint? Current,
    ProcessingFingerprint? Persisted,
    SessionStaleness Staleness);

using Sufni.App.Sessions.Processing.RecordedSessionProjection;

namespace Sufni.App.SyncAndPairing.Services;

internal enum ProcessedGenerationDecision { KeepLocal, WaitForSource, AcceptIncoming }

internal static class ProcessedGenerationAcceptance
{
    public static ProcessedGenerationDecision DecideProcessedGenerationTarget(
        ProcessingFingerprint? current,
        ProcessingFingerprint? held,
        ProcessingFingerprint? incoming,
        bool hasData,
        bool sourceAvailable,
        int schemaVersion)
    {
        if (incoming is null || incoming.SchemaVersion != schemaVersion ||
            (hasData && held == incoming))
            return ProcessedGenerationDecision.KeepLocal;
        if (!sourceAvailable) return ProcessedGenerationDecision.WaitForSource;
        return current is not null && incoming == current && (!hasData || held != current)
            ? ProcessedGenerationDecision.AcceptIncoming
            : ProcessedGenerationDecision.KeepLocal;
    }
}

using System.Collections.Generic;
using Sufni.App.SessionGraph;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public sealed class RecordedSessionListExtensionService : IRecordedSessionListExtensionService
{
    public IReadOnlyList<RecordedSessionListIndicatorContribution> CreateIndicators(RecordedSessionSummary summary)
    {
        return [];
    }

    public IReadOnlyList<RecordedSessionListActionContribution> CreateActions(RecordedSessionSummary summary)
    {
        return [];
    }
}

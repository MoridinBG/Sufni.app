using System.Collections.Generic;
using Sufni.App.SessionGraph;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public interface IRecordedSessionListExtensionService
{
    IReadOnlyList<RecordedSessionListIndicatorContribution> CreateIndicators(RecordedSessionSummary summary);
    IReadOnlyList<RecordedSessionListActionContribution> CreateActions(RecordedSessionSummary summary);
}

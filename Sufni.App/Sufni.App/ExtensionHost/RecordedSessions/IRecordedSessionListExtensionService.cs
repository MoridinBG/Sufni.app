using System;
using System.Collections.Generic;
using Sufni.App.SessionGraph;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public interface IRecordedSessionListContributionProvider
{
    IReadOnlyList<RecordedSessionListIndicatorContribution> CreateIndicators(RecordedSessionSummary summary);
    IReadOnlyList<RecordedSessionListActionContribution> CreateActions(RecordedSessionSummary summary);
}

public interface IRecordedSessionListContributionChangeSource
{
    event EventHandler? ContributionsChanged;
}

public interface IRecordedSessionListExtensionService
{
    event EventHandler? ContributionsChanged;

    IReadOnlyList<RecordedSessionListIndicatorContribution> CreateIndicators(RecordedSessionSummary summary);
    IReadOnlyList<RecordedSessionListActionContribution> CreateActions(RecordedSessionSummary summary);
}

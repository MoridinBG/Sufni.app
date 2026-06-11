using System;
using System.Collections.Generic;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;

namespace Sufni.App.ExtensionHost.Contracts.RecordedSessions;

public interface IRecordedSessionListContributionProvider
{
    string ExtensionId { get; }
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

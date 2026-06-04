using System.Collections.Generic;
using System.Linq;
using Sufni.App.SessionGraph;

namespace Sufni.App.ExtensionHost.RecordedSessions;

public sealed class RecordedSessionListExtensionService : IRecordedSessionListExtensionService
{
    private readonly IReadOnlyList<IRecordedSessionListContributionProvider> providers;

    public RecordedSessionListExtensionService(IEnumerable<IRecordedSessionListContributionProvider>? providers = null)
    {
        this.providers = providers?.ToArray() ?? [];
    }

    public IReadOnlyList<RecordedSessionListIndicatorContribution> CreateIndicators(RecordedSessionSummary summary)
    {
        return providers
            .SelectMany(provider => provider.CreateIndicators(summary))
            .OrderBy(contribution => contribution.Order)
            .ToArray();
    }

    public IReadOnlyList<RecordedSessionListActionContribution> CreateActions(RecordedSessionSummary summary)
    {
        return providers
            .SelectMany(provider => provider.CreateActions(summary))
            .OrderBy(contribution => contribution.Order)
            .ToArray();
    }
}

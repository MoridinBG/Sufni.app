using System;
using System.Collections.Generic;
using System.Linq;
using Sufni.App.ExtensionHost;
using Sufni.App.SessionGraph;

namespace Sufni.App.ExtensionHost.RecordedSessions;

internal sealed class RecordedSessionListExtensionService : IRecordedSessionListExtensionService
{
    private readonly IReadOnlyList<IRecordedSessionListContributionProvider> providers;

    public RecordedSessionListExtensionService(IEnumerable<IRecordedSessionListContributionProvider>? providers = null)
    {
        this.providers = providers?.ToArray() ?? [];
        ValidateProviders(this.providers);
        foreach (var changeSource in this.providers.OfType<IRecordedSessionListContributionChangeSource>())
        {
            changeSource.ContributionsChanged += OnProviderContributionsChanged;
        }
    }

    public event EventHandler? ContributionsChanged;

    public IReadOnlyList<RecordedSessionListIndicatorContribution> CreateIndicators(RecordedSessionSummary summary)
    {
        return CreateContributions(summary)
            .Indicators
            .OrderBy(contribution => contribution.Order)
            .ToArray();
    }

    public IReadOnlyList<RecordedSessionListActionContribution> CreateActions(RecordedSessionSummary summary)
    {
        return CreateContributions(summary)
            .Actions
            .OrderBy(contribution => contribution.Order)
            .ToArray();
    }

    private static void ValidateProviders(IReadOnlyList<IRecordedSessionListContributionProvider> providers)
    {
        var extensionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider);
            ExtensionContributionValidator.ValidateRequiredId(
                provider.ExtensionId,
                "Recorded-session list contribution provider");
            if (!extensionIds.Add(provider.ExtensionId))
            {
                throw new InvalidOperationException(
                    $"More than one recorded-session list contribution provider is registered for extension '{provider.ExtensionId}'.");
            }
        }
    }

    private RecordedSessionListContributionSnapshot CreateContributions(RecordedSessionSummary summary)
    {
        var indicators = new List<RecordedSessionListIndicatorContribution>();
        var actions = new List<RecordedSessionListActionContribution>();
        foreach (var provider in providers)
        {
            var providerIndicators = provider.CreateIndicators(summary);
            var providerActions = provider.CreateActions(summary);
            ExtensionContributionValidator.ValidateRecordedSessionListContributions(
                provider.ExtensionId,
                providerIndicators,
                providerActions);
            indicators.AddRange(providerIndicators);
            actions.AddRange(providerActions);
        }

        return new RecordedSessionListContributionSnapshot(indicators, actions);
    }

    private void OnProviderContributionsChanged(object? sender, EventArgs args)
    {
        ContributionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record RecordedSessionListContributionSnapshot(
        IReadOnlyList<RecordedSessionListIndicatorContribution> Indicators,
        IReadOnlyList<RecordedSessionListActionContribution> Actions);
}

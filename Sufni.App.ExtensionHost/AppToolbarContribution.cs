using System.Collections.Generic;

namespace Sufni.App.ExtensionHost;

public interface IAppToolbarContributionProvider
{
    string ExtensionId { get; }
    IReadOnlyList<AppToolbarContribution> CreateContributions();
}

public sealed record AppToolbarContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    IAppToolbarContributionViewModel ViewModel) : IExtensionContribution;

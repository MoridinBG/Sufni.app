using System.Collections.Generic;

namespace Sufni.App.ExtensionHost;

public interface IAppToolbarContributionProvider
{
    IReadOnlyList<AppToolbarContribution> CreateContributions();
}

public sealed record AppToolbarContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    object ViewModel);

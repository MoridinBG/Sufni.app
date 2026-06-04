namespace Sufni.App.ExtensionHost;

public sealed record AppToolbarContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    object ViewModel);

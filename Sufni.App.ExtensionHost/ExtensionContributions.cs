namespace Sufni.App.ExtensionHost;

public interface IExtensionContribution
{
    string ExtensionId { get; }
    string ContributionId { get; }
    int Order { get; }
}

public interface IExtensionViewModel;

public interface IAppToolbarContributionViewModel : IExtensionViewModel;

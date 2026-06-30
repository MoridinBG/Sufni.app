namespace Sufni.App.ExtensionHost.Contracts.Capabilities;

public interface IExtensionContribution
{
    string ExtensionId { get; }
    string ContributionId { get; }
    int Order { get; }
}

public interface IExtensionViewModel;

public interface IAppToolbarContributionViewModel : IExtensionViewModel;

public sealed record ToolbarIconDescriptor(
    string AssetPath,
    double Width = 18,
    double Height = 18);

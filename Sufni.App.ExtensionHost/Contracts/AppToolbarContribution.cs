using System.Collections.Generic;
using System.Windows.Input;

namespace Sufni.App.ExtensionHost.Contracts;

public interface IAppToolbarContributionProvider
{
    string ExtensionId { get; }
    IReadOnlyList<AppToolbarCommandContribution> CreateCommandContributions();
    IReadOnlyList<AppToolbarViewContribution> CreateViewContributions();
}

public sealed record AppToolbarCommandContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    string Label,
    ToolbarIconDescriptor? Icon,
    ICommand Command,
    object? CommandParameter = null) : IExtensionContribution;

public sealed record AppToolbarViewContribution(
    string ExtensionId,
    string ContributionId,
    int Order,
    IAppToolbarContributionViewModel ViewModel) : IExtensionContribution;

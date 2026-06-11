using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

namespace Sufni.App.ViewModels.SessionPages;

internal sealed class RecordedSessionExtensionPageViewModel(
    string displayName,
    IRecordedSessionPageContributionViewModel viewModel) : PageViewModelBase(displayName)
{
    public IRecordedSessionPageContributionViewModel ViewModel { get; } = viewModel;
}

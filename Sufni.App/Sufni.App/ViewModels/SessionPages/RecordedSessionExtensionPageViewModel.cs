using Sufni.App.ExtensionHost.Contracts;

namespace Sufni.App.ViewModels.SessionPages;

internal sealed class RecordedSessionExtensionPageViewModel(
    string displayName,
    IExtensionViewModel viewModel) : PageViewModelBase(displayName)
{
    public IExtensionViewModel ViewModel { get; } = viewModel;
}

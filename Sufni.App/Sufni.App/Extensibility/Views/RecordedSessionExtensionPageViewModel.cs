
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Extensibility.Views;

internal sealed class RecordedSessionExtensionPageViewModel(
    string displayName,
    IExtensionViewModel viewModel) : PageViewModelBase(displayName)
{
    public IExtensionViewModel ViewModel { get; } = viewModel;
}

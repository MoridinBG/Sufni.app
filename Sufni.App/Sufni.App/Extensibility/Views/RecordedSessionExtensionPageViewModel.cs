using System;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Extensibility.Views;

internal sealed class RecordedSessionExtensionPageViewModel(
    string displayName,
    Func<IExtensionViewModel> createViewModel) : PageViewModelBase(displayName)
{
    private readonly Func<IExtensionViewModel> createViewModel = createViewModel;
    private IExtensionViewModel? viewModel;

    public IExtensionViewModel ViewModel => viewModel ??= createViewModel();
}

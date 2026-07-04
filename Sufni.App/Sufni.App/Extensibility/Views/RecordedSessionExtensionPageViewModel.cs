using System;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Extensibility.Views;

internal sealed class RecordedSessionExtensionPageViewModel(
    string displayName,
    Func<IExtensionViewModel> createViewModel,
    bool ownsViewModel) : PageViewModelBase(displayName), IDisposable
{
    private readonly Func<IExtensionViewModel> createViewModel = createViewModel;
    private IExtensionViewModel? viewModel;
    private bool disposed;

    public IExtensionViewModel ViewModel => viewModel ??= createViewModel();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (ownsViewModel)
        {
            ExtensionViewModelLifetime.Dispose(viewModel);
        }

        viewModel = null;
    }
}

using System;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.Capabilities;

namespace Sufni.App.Extensibility.Views;

internal static class ExtensionViewModelLifetime
{
    public static void Dispose(IExtensionViewModel? viewModel)
    {
        if (viewModel is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    public static OwnedControl CreateOwnedControl(IExtensionViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var control = CreateControl(viewModel);
        return new OwnedControl(viewModel, control);
    }

    public static BorrowedControl CreateBorrowedControl(IExtensionViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        return new BorrowedControl(viewModel, CreateControl(viewModel));
    }

    public static Control CreateControl(IExtensionViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        return viewModel as Control ?? new ContentControl { Content = viewModel };
    }

    internal sealed record BorrowedControl(IExtensionViewModel ViewModel, Control Control);

    internal sealed record OwnedControl(IExtensionViewModel ViewModel, Control Control) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ExtensionViewModelLifetime.Dispose(ViewModel);
        }
    }
}

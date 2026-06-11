using System;
using System.Threading.Tasks;

namespace Sufni.App.ExtensionHost.Contracts.Services;

public interface IExtensionDialogService
{
    Task<TResult?> ShowDialogAsync<TResult>(ExtensionDialogRequest<TResult> request);
    Task<bool> ShowConfirmationAsync(string title, string message);
}

public interface IExtensionDialogResultSource<TResult>
{
    event EventHandler<TResult?> Completed;
}

public sealed record ExtensionDialogRequest<TResult>(
    string Title,
    IExtensionDialogResultSource<TResult> ViewModel,
    ExtensionDialogLayout Layout);

public sealed record ExtensionDialogLayout(
    double Width,
    double Height,
    double MinWidth,
    double MinHeight,
    bool CanResize);

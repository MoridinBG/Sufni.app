using System;

using Sufni.App.Shared.Base;
namespace Sufni.App.Shell.Coordinators;

/// <summary>
/// Shell-level navigation over the shared workspace tabs.
/// </summary>
public interface IShellCoordinator
{
    /// <summary>
    /// Open a view as the current workspace detail surface.
    /// </summary>
    void Open(ViewModelBase view);

    /// <summary>
    /// Open a view if no existing one of type <typeparamref name="T"/>
    /// matches <paramref name="match"/>; otherwise focus the existing one.
    /// The factory is only invoked when needed.
    /// </summary>
    void OpenOrFocus<T>(Func<T, bool> match, Func<T> create) where T : ViewModelBase;

    /// <summary>
    /// Open a view in the background if no existing one of type
    /// <typeparamref name="T"/> matches <paramref name="match"/>.
    /// </summary>
    void OpenInBackground<T>(Func<T, bool> match, Func<T> create) where T : ViewModelBase;

    /// <summary>
    /// Close a specific view when it is a workspace tab.
    /// </summary>
    void Close(ViewModelBase view);

    /// <summary>
    /// Close the first view of type <typeparamref name="T"/> matching
    /// <paramref name="match"/>, if any.
    /// </summary>
    void CloseIfOpen<T>(Func<T, bool> match, bool forgetRestoreHistory = false) where T : ViewModelBase;

    /// <summary>
    /// Return from the current workspace detail surface to the previous tab
    /// or primary shell surface. Returns whether the shell consumed the back
    /// request.
    /// </summary>
    bool GoBack();
}

using System;
using System.Threading.Tasks;

using Sufni.App.Shared.Base;
namespace Sufni.App.Shell.Coordinators;

/// <summary>
/// Shell-level navigation over the shared workspace tabs.
/// </summary>
public interface IShellCoordinator
{
    void OpenEditor(EditorKey key, bool background = false);

    Task CloseEditorAsync(EditorKey key);

    /// <summary>
    /// Open a view as the current workspace detail surface.
    /// </summary>
    void Open(ViewModelBase view);

    /// <summary>
    /// Close a specific view when it is a workspace tab.
    /// </summary>
    void Close(ViewModelBase view);

    /// <summary>
    /// Return from the current workspace detail surface to the previous tab
    /// or primary shell surface. Returns whether the shell consumed the back
    /// request.
    /// </summary>
    bool GoBack();

    /// <summary>Apply post-save navigation for a successfully saved editor.</summary>
    void CompleteEdit();
}

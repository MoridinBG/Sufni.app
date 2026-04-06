using System;
using Sufni.App.ViewModels;

namespace Sufni.App.Coordinators;

/// <summary>
/// Shell-level navigation. One implementation per application lifetime.
/// Desktop implementation manages tabs; mobile implementation manages a
/// navigation stack. Methods are no-ops on platforms where they have no
/// meaning (e.g. <see cref="GoBack"/> on desktop).
/// </summary>
public interface IShellCoordinator
{
    /// <summary>
    /// Open a view as a new top-level screen. On desktop this adds a tab
    /// and activates it. On mobile this pushes the view onto the stack.
    /// </summary>
    void Open(ViewModelBase view);

    /// <summary>
    /// Open a view if no existing one of type <typeparamref name="T"/>
    /// matches <paramref name="match"/>; otherwise focus the existing
    /// one. The factory is only invoked when needed. On desktop this
    /// prevents duplicate tabs for the same entity. On mobile —
    /// where navigation is a stack — the factory always runs and the
    /// new view is pushed.
    /// </summary>
    void OpenOrFocus<T>(Func<T, bool> match, Func<T> create) where T : ViewModelBase;

    /// <summary>
    /// Close a specific view. On desktop this removes its tab. On mobile
    /// this pops the stack only if <paramref name="view"/> is the current
    /// view; otherwise it does nothing.
    /// </summary>
    void Close(ViewModelBase view);

    /// <summary>
    /// Pop the current view on mobile (e.g. hardware back button). No-op
    /// on desktop.
    /// </summary>
    void GoBack();
}

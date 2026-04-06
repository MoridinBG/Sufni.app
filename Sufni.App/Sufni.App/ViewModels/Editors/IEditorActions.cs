using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Surface that the shared editor button strip
/// (<c>CommonButtonLine</c>) binds against. The new editor view models
/// inherit from <c>TabPageViewModelBase</c> directly (not from
/// <c>ItemViewModelBase</c>), so the strip can no longer rely on the
/// concrete base class for its command bindings. Each editor implements
/// this interface explicitly to forward the generated commands.
/// </summary>
public interface IEditorActions
{
    IRelayCommand OpenPreviousPageCommand { get; }
    IRelayCommand SaveCommand { get; }
    IRelayCommand ResetCommand { get; }
    IRelayCommand DeleteCommand { get; }
    IRelayCommand FakeDeleteCommand { get; }
}

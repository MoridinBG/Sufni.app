using Sufni.App.Shell.ViewModels.Base;
using Sufni.App.Shared.Base;

namespace Sufni.App.Shell.Coordinators;

public interface IEditorFactory
{
    TabPageViewModelBase? CreateEditor(EditorKey key, IShellCoordinator shell);
}

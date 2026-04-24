using Sufni.App.ViewModels.Editors;

namespace Sufni.App.ViewModels.SessionPages;

public sealed class LiveGraphPageViewModel : PageViewModelBase
{
    public ILiveSessionGraphWorkspace Workspace { get; }

    public LiveGraphPageViewModel(ILiveSessionGraphWorkspace workspace) : base("Graph")
    {
        Workspace = workspace;
    }
}

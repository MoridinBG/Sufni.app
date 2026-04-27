using Sufni.App.ViewModels.Editors;

namespace Sufni.App.ViewModels.SessionPages;

public sealed class LiveGraphPageViewModel : PageViewModelBase
{
    public ILiveSessionGraphWorkspace Workspace { get; }
    public ISessionMediaWorkspace MediaWorkspace { get; }

    public LiveGraphPageViewModel(ILiveSessionGraphWorkspace workspace, ISessionMediaWorkspace mediaWorkspace) : base("Graph")
    {
        Workspace = workspace;
        MediaWorkspace = mediaWorkspace;
    }
}

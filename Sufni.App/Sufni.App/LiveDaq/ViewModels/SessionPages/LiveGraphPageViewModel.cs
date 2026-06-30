
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.LiveDaq.ViewModels.SessionPages;

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

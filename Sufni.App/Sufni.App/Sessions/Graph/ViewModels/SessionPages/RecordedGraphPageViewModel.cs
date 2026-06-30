
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Sessions.Graph.ViewModels.SessionPages;

public sealed class RecordedGraphPageViewModel : PageViewModelBase
{
    public IRecordedSessionGraphWorkspace Workspace { get; }
    public ISessionMediaWorkspace MediaWorkspace { get; }

    public RecordedGraphPageViewModel(IRecordedSessionGraphWorkspace workspace, ISessionMediaWorkspace mediaWorkspace)
        : base("Graph")
    {
        Workspace = workspace;
        MediaWorkspace = mediaWorkspace;
    }
}
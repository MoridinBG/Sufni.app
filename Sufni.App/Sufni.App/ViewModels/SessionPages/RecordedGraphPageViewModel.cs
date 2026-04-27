using Sufni.App.ViewModels.Editors;

namespace Sufni.App.ViewModels.SessionPages;

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
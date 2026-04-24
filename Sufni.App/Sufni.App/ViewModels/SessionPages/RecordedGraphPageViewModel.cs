using Sufni.App.ViewModels.Editors;

namespace Sufni.App.ViewModels.SessionPages;

public sealed class RecordedGraphPageViewModel : PageViewModelBase
{
    public IRecordedSessionGraphWorkspace Workspace { get; }

    public RecordedGraphPageViewModel(IRecordedSessionGraphWorkspace workspace)
        : base("Graph")
    {
        Workspace = workspace;
    }
}
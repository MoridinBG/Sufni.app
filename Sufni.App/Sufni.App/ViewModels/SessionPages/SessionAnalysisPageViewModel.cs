using Sufni.App.ViewModels.Editors;

namespace Sufni.App.ViewModels.SessionPages;

public sealed class SessionAnalysisPageViewModel : PageViewModelBase
{
    public ISessionStatisticsWorkspace Workspace { get; }

    public SessionAnalysisPageViewModel(ISessionStatisticsWorkspace workspace)
        : base("Analysis")
    {
        Workspace = workspace;
    }
}

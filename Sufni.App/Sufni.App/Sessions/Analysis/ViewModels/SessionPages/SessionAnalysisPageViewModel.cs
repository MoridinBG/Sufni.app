
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Sessions.Analysis.ViewModels.SessionPages;

public sealed class SessionAnalysisPageViewModel : PageViewModelBase
{
    public ISessionStatisticsWorkspace Workspace { get; }

    public SessionAnalysisPageViewModel(ISessionStatisticsWorkspace workspace)
        : base("Analysis")
    {
        Workspace = workspace;
    }
}

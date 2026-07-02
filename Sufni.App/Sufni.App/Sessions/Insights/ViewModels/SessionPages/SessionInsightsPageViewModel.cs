
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Sessions.Insights.ViewModels.SessionPages;

public sealed class SessionInsightsPageViewModel : PageViewModelBase
{
    public ISessionAnalysisWorkspace Workspace { get; }

    public SessionInsightsPageViewModel(ISessionAnalysisWorkspace workspace)
        : base("Insights")
    {
        Workspace = workspace;
    }
}

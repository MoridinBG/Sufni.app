using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Presentation;

namespace Sufni.App.Sessions.Insights.ViewModels.SessionPages;

public sealed class BikeResponsePageViewModel(ISessionAnalysisWorkspace workspace) : PageViewModelBase("Bike response")
{
    public ISessionAnalysisWorkspace Workspace { get; } = workspace;
}

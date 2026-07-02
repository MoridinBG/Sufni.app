
using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public sealed class VibrationPageViewModel : PageViewModelBase
{
    public ISessionAnalysisWorkspace Workspace { get; }

    public VibrationPageViewModel(ISessionAnalysisWorkspace workspace)
        : base("Vibration")
    {
        Workspace = workspace;
    }
}

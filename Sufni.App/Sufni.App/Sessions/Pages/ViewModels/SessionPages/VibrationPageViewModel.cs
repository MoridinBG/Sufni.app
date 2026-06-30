
using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public sealed class VibrationPageViewModel : PageViewModelBase
{
    public ISessionStatisticsWorkspace Workspace { get; }

    public VibrationPageViewModel(ISessionStatisticsWorkspace workspace)
        : base("Vibration")
    {
        Workspace = workspace;
    }
}

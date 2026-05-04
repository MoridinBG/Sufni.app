using Sufni.App.ViewModels.Editors;

namespace Sufni.App.ViewModels.SessionPages;

public sealed class VibrationPageViewModel : PageViewModelBase
{
    public ISessionStatisticsWorkspace Workspace { get; }

    public VibrationPageViewModel(ISessionStatisticsWorkspace workspace)
        : base("Vibration")
    {
        Workspace = workspace;
    }
}

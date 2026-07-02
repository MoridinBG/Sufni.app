
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.LiveDaq.ViewModels.SessionPages;

public sealed class LiveSignalsPageViewModel : PageViewModelBase
{
    public ILiveSessionSignalsWorkspace Workspace { get; }
    public ISessionMediaWorkspace MediaWorkspace { get; }

    public LiveSignalsPageViewModel(ILiveSessionSignalsWorkspace workspace, ISessionMediaWorkspace mediaWorkspace) : base("Signals")
    {
        Workspace = workspace;
        MediaWorkspace = mediaWorkspace;
    }
}

using Sufni.App.Sessions.Presentation;

using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Sessions.Signals.ViewModels.SessionPages;

public sealed class RecordedSignalsPageViewModel : PageViewModelBase
{
    public IRecordedSessionSignalsWorkspace Workspace { get; }
    public ISessionMediaWorkspace MediaWorkspace { get; }

    public RecordedSignalsPageViewModel(IRecordedSessionSignalsWorkspace workspace, ISessionMediaWorkspace mediaWorkspace)
        : base("Signals")
    {
        Workspace = workspace;
        MediaWorkspace = mediaWorkspace;
    }
}
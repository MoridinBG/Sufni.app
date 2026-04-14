using Sufni.App.ViewModels;

namespace Sufni.App.ViewModels.Editors;

public sealed class LiveSessionGraphWorkspaceViewModel : ViewModelBase, ILiveSessionGraphWorkspace
{
    public SessionTimelineLinkViewModel Timeline { get; }

    public LiveSessionGraphWorkspaceViewModel()
        : this(new SessionTimelineLinkViewModel())
    {
    }

    public LiveSessionGraphWorkspaceViewModel(SessionTimelineLinkViewModel timeline)
    {
        Timeline = timeline;
    }
}
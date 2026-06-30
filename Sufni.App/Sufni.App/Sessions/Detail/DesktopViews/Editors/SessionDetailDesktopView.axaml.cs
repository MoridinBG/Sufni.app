using Avalonia.Controls;

namespace Sufni.App.Sessions.Detail.DesktopViews.Editors;

public partial class SessionDetailDesktopView : UserControl
{
    public SessionDetailDesktopView()
    {
        InitializeComponent();
        // Opening or re-activating the session tab must pull keyboard focus
        // off the sessions list, whose item would otherwise consume Space
        // as a selection key before the plot playback handler can see it.
        Loaded += (_, _) => Focus();
    }
}

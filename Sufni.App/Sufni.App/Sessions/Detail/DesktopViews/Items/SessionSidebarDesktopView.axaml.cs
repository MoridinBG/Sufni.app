using Avalonia.Controls;
using Sufni.App.Sessions.Detail.ViewModels.Editors;

namespace Sufni.App.Sessions.Detail.DesktopViews.Items;

public partial class SessionSidebarDesktopView : UserControl
{
    public SessionSidebarDesktopView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SyncPreferencesPage();
        SyncPreferencesPage();
    }

    private void SyncPreferencesPage()
    {
        PreferencesContent.DataContext = (DataContext as ISessionSidebarWorkspace)?.PreferencesPage;
    }
}

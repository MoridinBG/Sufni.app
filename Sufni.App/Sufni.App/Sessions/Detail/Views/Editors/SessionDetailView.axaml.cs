using Avalonia.Controls;
using Sufni.App.Sessions.Detail.ViewModels.Editors;

namespace Sufni.App.Sessions.Detail.Views.Editors;

public partial class SessionDetailView : UserControl
{
    public SessionDetailView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => SyncMobileShell();
        SyncMobileShell();
    }

    private void SyncMobileShell()
    {
        if (DataContext is SessionDetailViewModel viewModel)
        {
            MobileShell.DataContext = viewModel.MobileWorkspace;
            MobileShell.LoadedCommand = viewModel.LoadedCommand;
            MobileShell.UnloadedCommand = viewModel.UnloadedCommand;
            return;
        }

        MobileShell.DataContext = null;
        MobileShell.LoadedCommand = null;
        MobileShell.UnloadedCommand = null;
    }
}

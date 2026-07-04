using Avalonia;
using Avalonia.Controls;
using Sufni.App.LiveDaq.ViewModels.SessionPages;
using Sufni.App.Sessions.Media.Views;

namespace Sufni.App.LiveDaq.Views.SessionPages;

public partial class LiveSignalsPageView : UserControl
{
    private readonly MediaWorkspaceMapBinder mapBinder;

    public LiveSignalsPageView()
    {
        InitializeComponent();
        mapBinder = new MediaWorkspaceMapBinder(MobileMapView);
        DataContextChanged += (_, _) => SyncMediaWorkspace();
        SyncMediaWorkspace();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SyncMediaWorkspace();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        mapBinder.SetWorkspace(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SyncMediaWorkspace() =>
        mapBinder.SetWorkspace((DataContext as LiveSignalsPageViewModel)?.MediaWorkspace);
}

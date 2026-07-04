using Avalonia;
using Avalonia.Controls;
using Sufni.App.Sessions.Media.Views;
using Sufni.App.Sessions.Signals.ViewModels.SessionPages;

namespace Sufni.App.Sessions.Signals.Views.SessionPages;

public partial class RecordedSignalsPageView : UserControl
{
    private readonly MediaWorkspaceMapBinder mapBinder;

    public RecordedSignalsPageView()
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
        mapBinder.SetWorkspace((DataContext as RecordedSignalsPageViewModel)?.MediaWorkspace);
}

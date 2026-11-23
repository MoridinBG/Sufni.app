using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Sufni.App.Bikes.ViewModels.LinkageEditing;
using Avalonia.Controls.PanAndZoom;

using Sufni.App.Bikes.ViewModels.Editors;
namespace Sufni.App.Bikes.DesktopViews.Editors;

public partial class LinkageCanvasDesktopView : UserControl
{
    public LinkageCanvasDesktopView()
    {
        InitializeComponent();

        ZoomBorder.ZoomChanged += ZoomBorder_ZoomChanged;
        ZoomBorder.SizeChanged += ZoomBorder_SizeChanged;

        ZoomBorder.PanButton = ButtonName.Left;
        ZoomBorder.EnableConstrains = true;
        ZoomBorder.MinZoomX = 1.0;
        ZoomBorder.MinZoomY = 1.0;
        ZoomBorder.MaxOffsetX = 0.0;
        ZoomBorder.MaxOffsetY = 0.0;
    }

    private void Canvas_DoubleTapped(object? sender, TappedEventArgs args)
    {
        if (DataContext is not LinkageWorkspaceViewModel editor) return;
        var point = args.GetPosition(args.Source as Visual);
        editor.AddPoint(point.X, point.Y);
    }

    private void Canvas_Tapped(object? sender, TappedEventArgs args)
    {
        if (DataContext is LinkageWorkspaceViewModel editor)
            editor.ClearSelectionCommand.Execute(null);
    }

    private void Point_Tapped(object? sender, TappedEventArgs args)
    {
        if (DataContext is not LinkageWorkspaceViewModel editor || sender is not Control { DataContext: JointRowViewModel point }) return;
        editor.SelectPointCommand.Execute(point);
        args.Handled = true;
    }

    private void Link_Tapped(object? sender, TappedEventArgs args)
    {
        if (DataContext is not LinkageWorkspaceViewModel editor || sender is not Control { DataContext: LinkRowViewModel link }) return;
        editor.SelectLinkCommand.Execute(link);
        args.Handled = true;
    }

    private void ZoomBorder_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ZoomBorder.MinOffsetX = -e.NewSize.Width * (ZoomBorder.ZoomX - 1.0);
        ZoomBorder.MinOffsetY = -e.NewSize.Height * (ZoomBorder.ZoomY - 1.0);
    }

    private void ZoomBorder_ZoomChanged(object sender, ZoomChangedEventArgs e)
    {
        ZoomBorder.MinOffsetX = -ZoomBorder.Bounds.Width * (e.ZoomX - 1.0);
        ZoomBorder.MinOffsetY = -ZoomBorder.Bounds.Height * (e.ZoomY - 1.0);

        if (DataContext is LinkageWorkspaceViewModel vm)
        {
            vm.UpdateZoomPresentation(e.ZoomX);
        }
    }
}

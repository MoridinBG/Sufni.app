using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Sufni.App.Bikes.ViewModels.LinkageParts;
using Avalonia.Controls.PanAndZoom;

using Sufni.App.Bikes.ViewModels.Editors;
namespace Sufni.App.Bikes.DesktopViews.Items;

public partial class BikeImageDesktopView : UserControl
{
    public BikeImageDesktopView()
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
        if (DataContext is not BikeEditorViewModel editor) return;
        var point = args.GetPosition(args.Source as Visual);
        editor.LinkageEditor.AddPoint(point.X, point.Y);
    }

    private void Canvas_Tapped(object? sender, TappedEventArgs args)
    {
        if (DataContext is BikeEditorViewModel editor)
            editor.LinkageEditor.ClearSelectionCommand.Execute(null);
    }

    private void Point_Tapped(object? sender, TappedEventArgs args)
    {
        if (DataContext is not BikeEditorViewModel editor || sender is not Control { DataContext: JointViewModel point }) return;
        editor.LinkageEditor.SelectPointCommand.Execute(point);
        args.Handled = true;
    }

    private void Link_Tapped(object? sender, TappedEventArgs args)
    {
        if (DataContext is not BikeEditorViewModel editor || sender is not Control { DataContext: LinkViewModel link }) return;
        editor.LinkageEditor.SelectLinkCommand.Execute(link);
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

        if (DataContext is BikeEditorViewModel vm)
        {
            vm.LinkageEditor.UpdateZoomPresentation(e.ZoomX);
        }
    }
}

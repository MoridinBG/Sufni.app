using Avalonia.Controls;
using Avalonia.Controls.PanAndZoom;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.DesktopViews.Items;

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
            vm.LinkStrokeThickness = 45.0 / e.ZoomX;
            var taper = 0.65 + 0.35 * (1.0 - 1.0 / e.ZoomX);
            vm.JointFontSize = 120.0 * taper / e.ZoomX;
        }
    }
}
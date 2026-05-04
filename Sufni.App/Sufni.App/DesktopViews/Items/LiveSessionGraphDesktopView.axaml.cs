using Avalonia.Controls;

namespace Sufni.App.DesktopViews.Items;

public partial class LiveSessionGraphDesktopView : UserControl
{
    public LiveSessionGraphDesktopView()
    {
        InitializeComponent();
        SessionGraphGridSizing.AttachEqualHeightReset(
            this.FindControl<Grid>("GraphGrid")!,
            this.FindControl<GridSplitter>("FirstGraphSplitter"),
            this.FindControl<GridSplitter>("SecondGraphSplitter"),
            this.FindControl<GridSplitter>("ThirdGraphSplitter"),
            this.FindControl<GridSplitter>("FourthGraphSplitter"));
    }
}

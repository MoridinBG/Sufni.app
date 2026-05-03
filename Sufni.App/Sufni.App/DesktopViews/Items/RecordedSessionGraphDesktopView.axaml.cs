using Avalonia.Controls;

namespace Sufni.App.DesktopViews.Items;

public partial class RecordedSessionGraphDesktopView : UserControl
{
    public RecordedSessionGraphDesktopView()
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

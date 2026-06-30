
using Sufni.App.Bikes.Views.Items;
using Sufni.App.Bikes.ViewModels.Editors;
namespace Sufni.App.Bikes.DesktopViews.Editors;

public partial class BikeEditorDesktopView : BikeViewBase
{
    public BikeEditorDesktopView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => ApplyDesktopCapabilities();
    }

    private void ApplyDesktopCapabilities()
    {
        if (DataContext is BikeEditorViewModel editor)
        {
            editor.CanChangeRearSuspensionMode = true;
        }
    }
}

using Sufni.App.Views.Items;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.DesktopViews.Editors;

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

using Avalonia.Controls;
using Avalonia.Input;
using Sufni.App.ViewModels.LinkageParts;

namespace Sufni.App.Views.LinkageParts;

public partial class JointView : UserControl
{
    public JointView()
    {
        InitializeComponent();

        // Mark PointerPressed as handled, so that dragging the item won't induce panning too.
        PointerPressed += (_, e) => e.Handled = true;
        
        // Indicate that the Joint might have been dragged if the left mouse button is released on it.
        PointerReleased += (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left && DataContext is JointViewModel jvm)
            {
                jvm.WasPossiblyDragged = true;
            }
        };

        // Show flyout when a new, modifiable point is added.
        Loaded += (_, _) =>
        {
            if (DataContext is JointViewModel { Immutability: Immutability.Modifiable, ShowFlyout: true } jvm)
            {
                PointCanvas.ContextFlyout!.ShowAt(PointCanvas);
                jvm.ShowFlyout = false;
            }
        };
    }

    private void TextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PointCanvas.ContextFlyout!.Hide();
        }
    }
}
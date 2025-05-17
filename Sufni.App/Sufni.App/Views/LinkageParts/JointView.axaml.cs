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

        // Show flyout when a new, modifyable point is added.
        Loaded += (_, _) =>
        {
            if (DataContext is JointViewModel { Immutability: Immutability.Modifiable})
            {
                PointCanvas.ContextFlyout!.ShowAt(PointCanvas);
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
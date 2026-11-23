using Avalonia.Controls;
using Avalonia.Input;

using Sufni.App.Bikes.ViewModels.LinkageEditing;
namespace Sufni.App.Bikes.Views.Editors;

public partial class JointView : UserControl
{
    public JointView()
    {
        InitializeComponent();

        // Mark PointerPressed as handled, so that dragging the item won't induce panning too.
        PointerPressed += (_, e) => e.Handled = true;
        
        Loaded += (_, _) =>
        {
            if (DataContext is JointRowViewModel { EditRule: JointEditRule.Modifiable, ShowFlyout: true } jvm)
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
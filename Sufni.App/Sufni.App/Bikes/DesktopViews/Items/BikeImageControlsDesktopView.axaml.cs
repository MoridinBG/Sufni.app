using Avalonia.Controls;

using Sufni.App.Bikes.ViewModels.LinkageEditing;
namespace Sufni.App.Bikes.DesktopViews.Items;

public partial class BikeImageControlsDesktopView : UserControl
{
    public BikeImageControlsDesktopView()
    {
        InitializeComponent();
    }

    private void DataGrid_OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.DataContext is JointRowViewModel { EditRule: JointEditRule.Immutable } ||
            e.Row.DataContext is LinkRowViewModel { IsImmutable: true })
        {
            e.Cancel = true;
        }

        if (e.Row.DataContext is JointRowViewModel { EditRule: JointEditRule.FixedName } && e.Column.DisplayIndex == 0)
        {
            e.Cancel = true;
        }
    }
}
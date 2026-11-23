using Avalonia.Controls;

using Sufni.App.Bikes.ViewModels.LinkageEditing;
namespace Sufni.App.Bikes.DesktopViews.Editors;

public partial class LinkageControlsDesktopView : UserControl
{
    public LinkageControlsDesktopView()
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

        if (e.Row.DataContext is JointRowViewModel { EditRule: JointEditRule.FixedName } && sender is DataGrid grid && e.Column == grid.Columns[0])
        {
            e.Cancel = true;
        }
    }
}
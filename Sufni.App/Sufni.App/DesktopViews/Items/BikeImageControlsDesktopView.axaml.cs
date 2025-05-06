using Avalonia.Controls;
using Sufni.App.ViewModels.LinkageParts;

namespace Sufni.App.DesktopViews.Items;

public partial class BikeImageControlsDesktopView : UserControl
{
    public BikeImageControlsDesktopView()
    {
        InitializeComponent();
    }

    private void DataGrid_OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row.DataContext is JointViewModel { Immutability: Immutability.Immutable } ||
            e.Row.DataContext is LinkViewModel { IsImmutable: true })
        {
            e.Cancel = true;
        }

        if (e.Row.DataContext is JointViewModel { Immutability: Immutability.NameOnly } && e.Column.DisplayIndex == 0)
        {
            e.Cancel = true;
        }
    }
}
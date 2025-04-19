using Avalonia.Controls;

namespace Sufni.App.Views.Controls;

public partial class EditableTitle : UserControl
{
    public EditableTitle()
    {
        InitializeComponent();

        TitleTextBox.LostFocus += (_, _) =>
        {
            TitleTextBox.ClearSelection();
            TitleTextBox.IsEnabled = false;
        };

        EditButton.Click += (_, _) =>
        {
            if (TitleTextBox.IsEnabled)
            {
                TitleTextBox.ClearSelection();
                TitleTextBox.IsEnabled = false;
            }
            else
            {
                TitleTextBox.IsEnabled = true;
                TitleTextBox.Focus();
                TitleTextBox.SelectAll();
            }
        };
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Sufni.App.Shared.Views.Controls;

public partial class CommonButtonLine : UserControl
{
    public CommonButtonLine()
    {
        InitializeComponent();
    }

    public void CancelButton_Click(object? sender, RoutedEventArgs args)
    {
        DeleteButton?.Flyout?.Hide();
    }
}
